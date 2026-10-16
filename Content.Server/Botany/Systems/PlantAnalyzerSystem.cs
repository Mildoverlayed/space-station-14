using System.Linq;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Content.Server.Botany.Components;
using Content.Shared.Atmos;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Botany.Traits.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.PowerCell;
using Content.Shared._NF.PlantAnalyzer;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Server.Botany.Systems;

public sealed class PlantAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly PowerCellSystem _cell = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly PlantTraySystem _plantTray = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlantAnalyzerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PlantAnalyzerComponent, PlantAnalyzerDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<PlantAnalyzerComponent, PlantAnalyzerSetMode>(OnModeSelected);
    }

    private void OnAfterInteract(Entity<PlantAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Target == null
            || !args.CanReach
            || !_cell.HasActivatableCharge(ent.Owner, user: args.User)
            || ent.Comp.DoAfter != null
            || !CanScan(args.Target.Value, out _, out _))
        {
            return;
        }

        var delay = ent.Comp.Settings.AdvancedScan
            ? ent.Comp.Settings.AdvScanDelay
            : ent.Comp.Settings.ScanDelay;
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            delay,
            new PlantAnalyzerDoAfterEvent(),
            ent,
            target: args.Target,
            used: ent)
        {
            NeedHand = true,
            BreakOnDamage = true,
            BreakOnMove = true,
            MovementThreshold = 0.01f
        };

        _doAfterSystem.TryStartDoAfter(doAfterArgs, out ent.Comp.DoAfter);
    }

    private void OnDoAfter(Entity<PlantAnalyzerComponent> ent, ref PlantAnalyzerDoAfterEvent args)
    {
        ent.Comp.DoAfter = null;
        if (args.Handled || args.Cancelled || args.Args.Target is not { } target
            || !CanScan(target, out var plant, out var isTray))
            return;

        if (ent.Comp.Settings.AdvancedScan && !_cell.TryUseActivatableCharge(ent.Owner, user: args.User))
            return;

        if (!_cell.TryUseActivatableCharge(ent.Owner, user: args.User))
            return;

        _audio.PlayPvs(ent.Comp.ScanningEndSound, ent);
        OpenUserInterface(args.User, ent);
        SendScannedData(ent, target, plant, isTray);
        args.Handled = true;
    }

    private bool CanScan(EntityUid target, out EntityUid plant, out bool isTray)
    {
        plant = target;
        isTray = false;

        if (HasComp<SeedComponent>(target))
            return true;

        if (TryComp<PlantTrayComponent>(target, out var tray))
        {
            if (!_plantTray.TryGetPlant((target, tray), out var trayPlant))
                return false;

            plant = trayPlant.Value;
            isTray = true;
            return true;
        }

        return HasComp<PlantHolderComponent>(target) && HasComp<PlantComponent>(target);
    }

    private void OpenUserInterface(EntityUid user, EntityUid analyzer)
    {
        if (!TryComp<ActorComponent>(user, out var actor)
            || !_uiSystem.HasUi(analyzer, PlantAnalyzerUiKey.Key))
        {
            return;
        }

        _uiSystem.OpenUi(analyzer, PlantAnalyzerUiKey.Key, actor.PlayerSession);
    }

    private void SendScannedData(
        Entity<PlantAnalyzerComponent> analyzer,
        EntityUid scannedTarget,
        EntityUid plant,
        bool isTray)
    {
        if (!_uiSystem.HasUi(analyzer, PlantAnalyzerUiKey.Key))
            return;

        EntityUid? snapshot = null;
        EntProtoId? plantProtoId = null;
        var dataEntity = plant;

        var isSeed = false;
        if (TryComp<SeedComponent>(scannedTarget, out var seed) && seed != null)
        {
            isSeed = true;
            dataEntity = scannedTarget;
            snapshot = seed.PlantData;
            plantProtoId = seed.PlantProtoId;
        }

        if (!TryPlantComponent<PlantComponent>(dataEntity, snapshot, plantProtoId, out var plantData)
            || !TryPlantComponent<PlantDataComponent>(dataEntity, snapshot, plantProtoId, out var plantInfo))
        {
            return;
        }

        var harvestType = AnalyzerHarvestType.Unknown;
        if (TryPlantComponent<PlantHarvestComponent>(dataEntity, snapshot, plantProtoId, out var harvest))
        {
            harvestType = harvest.HarvestRepeat switch
            {
                HarvestType.Repeat => AnalyzerHarvestType.Repeat,
                HarvestType.NoRepeat => AnalyzerHarvestType.NoRepeat,
                HarvestType.SelfHarvest => AnalyzerHarvestType.SelfHarvest,
                _ => AnalyzerHarvestType.Unknown
            };
        }

        var chemicals = TryPlantComponent<PlantChemicalsComponent>(dataEntity, snapshot, plantProtoId, out var chem)
            ? chem.Chemicals.Keys.Select(id => id.ToString()).ToArray()
            : [];
        var consumeGases = TryPlantComponent<PlantConsumeExudeGasComponent>(dataEntity, snapshot, plantProtoId, out var gases)
            ? GetGasFlags(gases.ConsumeGasses.Keys)
            : GasFlags.None;
        var exudeGases = gases == null ? GasFlags.None : GetGasFlags(gases.ExudeGasses.Keys);

        var mutationNames = new List<string>();
        foreach (var mutation in plantInfo.MutationPrototypes)
        {
            if (_botany.TryGetPlantComponent<PlantDataComponent>(null, mutation, out var mutationData))
                mutationNames.Add(mutationData.Name);
        }

        var message = new PlantAnalyzerScannedSeedPlantInformation
        {
            TargetEntity = GetNetEntity(plant),
            IsTray = isTray || !isSeed,
            SeedName = plantInfo.Name,
            SeedChem = chemicals,
            HarvestType = harvestType,
            ExudeGases = exudeGases,
            ConsumeGases = consumeGases,
            Endurance = plantData.Endurance,
            SeedYield = plantData.Yield,
            Lifespan = plantData.Lifespan,
            Maturation = plantData.Maturation,
            Production = plantData.Production,
            GrowthStages = plantData.GrowthStages,
            SeedPotency = plantData.Potency,
            Speciation = mutationNames.ToArray()
        };

        if (analyzer.Comp.Settings.AdvancedScan)
            message.AdvancedInfo = GetAdvancedInfo(dataEntity, snapshot, plantProtoId);

        _uiSystem.ServerSendUiMessage(analyzer.Owner, PlantAnalyzerUiKey.Key, message);
    }

    private bool TryPlantComponent<T>(
        EntityUid entity,
        EntityUid? snapshot,
        EntProtoId? plantProtoId,
        [NotNullWhen(true)] out T? component)
        where T : class, IComponent, new()
    {
        if (snapshot != null || plantProtoId != null)
            return _botany.TryGetPlantComponent(snapshot, plantProtoId, out component);

        return TryComp(entity, out component);
    }

    private AdvancedScanInfo GetAdvancedInfo(EntityUid entity, EntityUid? snapshot, EntProtoId? plantProtoId)
    {
        var result = new AdvancedScanInfo();

        if (TryPlantComponent<PlantGrowthComponent>(entity, snapshot, plantProtoId, out var growth))
        {
            result.NutrientConsumption = growth.NutrientConsumption;
            result.WaterConsumption = growth.WaterConsumption;
        }

        if (TryPlantComponent<PlantAtmosphericComponent>(entity, snapshot, plantProtoId, out var atmosphere))
        {
            result.IdealHeat = (atmosphere.LowHeatTolerance + atmosphere.HighHeatTolerance) / 2f;
            result.HeatTolerance = (atmosphere.HighHeatTolerance - atmosphere.LowHeatTolerance) / 2f;
            result.LowPressureTolerance = atmosphere.LowPressureTolerance;
            result.HighPressureTolerance = atmosphere.HighPressureTolerance;
        }

        if (TryPlantComponent<PlantToxinsComponent>(entity, snapshot, plantProtoId, out var toxins))
            result.ToxinsTolerance = toxins.ToxinsTolerance;

        if (TryPlantComponent<PlantWeedPestComponent>(entity, snapshot, plantProtoId, out var weedPest))
        {
            result.PestTolerance = weedPest.PestTolerance;
            result.WeedTolerance = weedPest.WeedTolerance;
        }

        if (TryPlantComponent<PlantTraitKudzuComponent>(entity, snapshot, plantProtoId, out _))
            result.Mutations |= MutationFlags.TurnIntoKudzu;
        if (TryPlantComponent<PlantTraitSeedlessComponent>(entity, snapshot, plantProtoId, out _))
            result.Mutations |= MutationFlags.Seedless;
        if (TryPlantComponent<PlantTraitLigneousComponent>(entity, snapshot, plantProtoId, out _))
            result.Mutations |= MutationFlags.Ligneous;
        if (TryPlantComponent<PlantTraitScreamComponent>(entity, snapshot, plantProtoId, out _))
            result.Mutations |= MutationFlags.CanScream;

        return result;
    }

    private static GasFlags GetGasFlags(IEnumerable<Gas> gases)
    {
        var flags = GasFlags.None;
        foreach (var gas in gases)
        {
            flags |= gas switch
            {
                Gas.Nitrogen => GasFlags.Nitrogen,
                Gas.Oxygen => GasFlags.Oxygen,
                Gas.CarbonDioxide => GasFlags.CarbonDioxide,
                Gas.Plasma => GasFlags.Plasma,
                Gas.Tritium => GasFlags.Tritium,
                Gas.WaterVapor => GasFlags.WaterVapor,
                Gas.Ammonia => GasFlags.Ammonia,
                Gas.NitrousOxide => GasFlags.NitrousOxide,
                Gas.Frezon => GasFlags.Frezon,
                _ => GasFlags.None
            };
        }

        return flags;
    }

    private void OnModeSelected(Entity<PlantAnalyzerComponent> ent, ref PlantAnalyzerSetMode args)
    {
        if (ent.Comp.DoAfter == null)
            ent.Comp.Settings.AdvancedScan = args.AdvancedScan;
    }
}
