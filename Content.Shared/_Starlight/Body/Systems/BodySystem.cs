// SPDX-FileCopyrightText: 2026 Starlight Network
// SPDX-License-Identifier: MIT

using Content.Shared._Starlight.Body.Components;
using Content.Shared._Starlight.Body.Editor;
using Content.Shared._Starlight.Body.Events;
using Content.Shared._Starlight.Body.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
namespace Content.Shared._Starlight.Body.Systems;

public sealed partial class SLBodySystem : EntitySystem
{
    public const string BodyContainerId = "sl_childbodyparts";
    [Dependency] private readonly SharedContainerSystem ContainerSystem = default!;
    [Dependency] private readonly IPrototypeManager Proto = default!;
    [Dependency] private readonly BodyPartVisualizerSystem BodyPartVisualizer = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SLBodyComponent, MapInitEvent>(OnBodyMapInit);
        SubscribeLocalEvent<SLBodyComponent, AfterAutoHandleStateEvent>(OnBodyHandleState);
        SubscribeLocalEvent<SLBodyPartComponent, AfterAutoHandleStateEvent>(OnPartHandleState);
        SubscribeLocalEvent<SLBodyPartComponent, SLBodyPartAddedEvent>(OnPartAdded);
        SubscribeLocalEvent<SLBodyPartComponent, SLBodyPartRemovedEvent>(OnPartRemoved);
    }

    /// <summary>
    /// All parts of the body, root first, parents before children. Rebuilt from the part tree on demand,
    /// so it is valid on the client as well. Do not attach or detach parts while iterating it.
    /// </summary>
    public IReadOnlyList<Entity<SLBodyPartComponent>> GetBodyParts(Entity<SLBodyComponent> body)
    {
        if (body.Comp.BodyPartsDirty)
            RebuildBodyPartCache(body);
        return body.Comp.BodyParts;
    }

    private void RebuildBodyPartCache(Entity<SLBodyComponent> body)
    {
        body.Comp.BodyParts.Clear();
        body.Comp.BodyPartsDirty = false;
        if (TryComp<SLBodyPartComponent>(body.Comp.RootPartEntity, out var root))
            CollectBodyParts(body, (body.Comp.RootPartEntity, root));
    }

    private void CollectBodyParts(Entity<SLBodyComponent> body, Entity<SLBodyPartComponent> part)
    {
        body.Comp.BodyParts.Add(part);
        if (!ContainerSystem.TryGetContainer(part, BodyContainerId, out var children))
            return;
        foreach (var child in children.ContainedEntities)
        {
            if (TryComp<SLBodyPartComponent>(child, out var childComp))
                CollectBodyParts(body, (child, childComp));
        }
    }

    private void MarkBodyPartsDirty(EntityUid body)
    {
        if (TryComp<SLBodyComponent>(body, out var bodyComp))
            bodyComp.BodyPartsDirty = true;
    }

    private void OnBodyHandleState(Entity<SLBodyComponent> body, ref AfterAutoHandleStateEvent args)
        => body.Comp.BodyPartsDirty = true;

    private void OnPartHandleState(Entity<SLBodyPartComponent> part, ref AfterAutoHandleStateEvent args)
        => MarkBodyPartsDirty(part.Comp.Body);

    private void OnPartAdded(Entity<SLBodyPartComponent> part, ref SLBodyPartAddedEvent args)
        => MarkBodyPartsDirty(part.Comp.Body);

    private void OnPartRemoved(Entity<SLBodyPartComponent> part, ref SLBodyPartRemovedEvent args)
        => MarkBodyPartsDirty(part.Comp.Body);
    private void OnBodyMapInit(Entity<SLBodyComponent> body, ref MapInitEvent args)
    {
        if (body.Comp.BodyBuilt) //We don't want to initialize body twice!
            return;
        if (BuildBody(body))
            Dirty(body);
    }

    public bool BuildBody(Entity<SLBodyComponent> body)
    {
        if (body.Comp.BodyBuilt ||  !Proto.TryIndex(body.Comp.Prefab, out var prefabProto))
            return false; //body has already been built, or no valid prefab :(
        MakeBodyParts(body, prefabProto);
        body.Comp.BodyBuilt = true;
        body.Comp.BodyPartsDirty = true;
        Dirty(body);
        foreach (var part in GetBodyParts(body))
            RaiseEnteredBody(body, part);
        RaiseLocalEvent(body, new BodyInitEvent());
        return true;
    }

    private Entity<SLBodyPartComponent> MakeBodyParts(Entity<SLBodyComponent> body, BodyPrefabPrototype prefab)
    {
        body.Comp.RootPartEntity = EntityManager.SpawnAttachedTo(prefab.Root.BodyPart, new(body.Owner, body.Comp.RootOffset));
        var bodyPart = new Entity<SLBodyPartComponent>(body.Comp.RootPartEntity,
            Comp<SLBodyPartComponent>(body.Comp.RootPartEntity));
        RecursivelyBuildBodyParts(body, null, bodyPart, prefab.Root, BodyPartAddress.Root);
        Dirty(body);
        Dirty(bodyPart);
        return bodyPart;
    }

    private void RecursivelyBuildBodyParts(Entity<SLBodyComponent> body, BodyPartContainer? parentContainer,Entity<SLBodyPartComponent> newPart,
        BodyPartDef partDef,
        BodyPartAddress address)
    {
        var children = EnsureChildren(newPart); //Create the root BodyPart container and ensure that sockets are properly setup!
        newPart.Comp.Body = body;
        if (partDef.AttachedParts != null)
            BuildChildParts(body, children, newPart, partDef.AttachedParts, address, internalSockets: false);
        if (partDef.ContainedParts != null)
            BuildChildParts(body, children, newPart, partDef.ContainedParts, address, internalSockets: true);
        BodyPartVisualizer.BakeAppearance(body, newPart, address);
        Dirty(newPart);
        if (parentContainer != null)
        {
            //Make sure to insert the body part LAST
            ContainerSystem.Insert(newPart.Owner, parentContainer, force: true);
        }
        else
        {
            // Root part is never inserted into a container, raise the event manually
            var ev = new SLBodyPartAddedEvent(newPart);
            RaiseLocalEvent(newPart, ref ev);
        }
    }

    private void BuildChildParts(Entity<SLBodyComponent> body,
        BodyPartContainer children,
        Entity<SLBodyPartComponent> parent,
        Dictionary<string, BodyPartDef> defs,
        BodyPartAddress address,
        bool internalSockets)
    {
        foreach (var (socketId, def) in defs)
        {
            if (!parent.Comp.Sockets.TryGetValue(socketId, out var socketType))
            {
                Log.Error($"Body {ToPrettyString(body)}: part {ToPrettyString(parent)} has no socket '{socketId}' for {def.BodyPart}");
                continue;
            }

            if (Proto.Index(socketType).Internal != internalSockets)
            {
                Log.Error($"Body {ToPrettyString(body)}: socket '{socketId}' on {ToPrettyString(parent)} is " +
                          $"{(internalSockets ? "external" : "internal")}, list {def.BodyPart} under " +
                          $"{(internalSockets ? "attachedParts" : "containedParts")}");
                continue;
            }

            var newChild = EntityManager.SpawnAttachedTo(def.BodyPart, new EntityCoordinates(parent, 0, 0));
            var childPart = new Entity<SLBodyPartComponent>(newChild, Comp<SLBodyPartComponent>(newChild));
            childPart.Comp.ParentSocket = new BodyPartSocket(socketId, socketType);
            childPart.Comp.Parent = parent;
            RecursivelyBuildBodyParts(body, children, childPart, def, address.Append(socketId));
        }
    }

    private BodyPartContainer EnsureChildren(Entity<SLBodyPartComponent> bodyPart)
    {
        if (bodyPart.Comp.Children != null)
            return bodyPart.Comp.Children;
        var exists = false;
        bodyPart.Comp.Children ??= ContainerSystem.EnsureContainer<BodyPartContainer>(bodyPart.Owner, BodyContainerId, out exists);
        if (!exists) //We created a new child container and need to populate its sockets
            PopulateSockets(bodyPart);
        return bodyPart.Comp.Children;
    }

    private void PopulateSockets(Entity<SLBodyPartComponent> bodyPart)
    {
        if (bodyPart.Comp.Children == null)
            EnsureChildren(bodyPart);
        var container = bodyPart.Comp.Children!;
        foreach (var (id, socketTypeId) in bodyPart.Comp.Sockets)
        {
            var socketType = Proto.Index(socketTypeId);
            container.TryRegisterSocket(new BodyPartSocket(id, socketType.ID));
        }
    }
}
