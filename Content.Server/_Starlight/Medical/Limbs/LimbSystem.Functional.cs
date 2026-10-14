using Content.Shared._Starlight.Body.Components;
using Content.Shared._Starlight.Medical.Body.Part;
using Content.Shared._Starlight.Medical.Limbs;
using Content.Shared.Body.Organ;
using Content.Shared.Humanoid;
using Content.Shared._Starlight.Medical.Surgery.Events;

namespace Content.Server._Starlight.Medical.Limbs;
public sealed partial class LimbSystem : SharedLimbSystem
{
    // Hands come from BodyPartHand parts and leg speed from MovementBodyPart legs (SLBody); organs entering the
    // body raise SurgeryOrganImplantationCompleted through the body bridge. What is left here is the humanoid
    // visuals of the attached subtree and the limb events.
    private void AddLimb(Entity<HumanoidAppearanceComponent> body, Entity<SLBodyPartComponent> limb)
    {
        if (TryComp<SLBodyPartComponent>(limb, out var slLimb))
        {
            foreach (var part in _slBody.GetSubtree((limb, slLimb)))
            {
                if (part.Owner != limb.Owner
                    && TryComp(part, out SLBodyPartComponent? inner)
                    && inner.Kind is BodyPartType.Hand or BodyPartType.Foot)
                    AddLimbVisual(body, (part, inner));
            }
        }

        RaiseLimbAttachedEvent(body, limb);
    }

    private void RaiseLimbAttachedEvent(Entity<HumanoidAppearanceComponent> body, Entity<SLBodyPartComponent> limb)
    {
        var @event = new LimbAttachedEvent
        {
            Limb = limb,
            Body = body
        };
        RaiseLocalEvent(body, ref @event);
        RaiseLocalEvent(limb, ref @event);
    }

    private void RemoveLimb(Entity<TransformComponent, HumanoidAppearanceComponent, SLBodyComponent> body, Entity<TransformComponent, MetaDataComponent, SLBodyPartComponent> limb)
    {
        RaiseLimbPreDetachEvent(body, limb);

        // Organs held by hands of the removed subtree (implants) leave with it.
        if (TryComp<SLBodyPartComponent>(limb, out var slLimb))
        {
            foreach (var part in _slBody.GetSubtree((limb, slLimb)))
            {
                if (!HasComp<OrganComponent>(part)
                    || part.Comp.Parent is not { } holder
                    || CompOrNull<SLBodyPartComponent>(holder)?.Kind != BodyPartType.Hand)
                    continue;

                var ev = new SurgeryOrganExtracted(body, holder, part);
                RaiseLocalEvent(part, ref ev);
            }
        }

        RaiseLimbDetachedEvent(body, limb);
    }

    private void RaiseLimbDetachedEvent(
        Entity<TransformComponent, HumanoidAppearanceComponent, SLBodyComponent> body,
        Entity<TransformComponent, MetaDataComponent, SLBodyPartComponent> limb)
    {
        var @event = new LimbDetachedEvent
        {
            Limb = limb,
            Body = body
        };
        RaiseLocalEvent(body, ref @event);
        RaiseLocalEvent(limb, ref @event);
    }
    private void RaiseLimbPreDetachEvent(
        Entity<TransformComponent, HumanoidAppearanceComponent, SLBodyComponent> body,
        Entity<TransformComponent, MetaDataComponent, SLBodyPartComponent> limb)
    {
        var @event = new LimbPreDetachEvent
        {
            Limb = limb,
            Body = body
        };
        RaiseLocalEvent(body, ref @event);
        RaiseLocalEvent(limb, ref @event);
    }
}
