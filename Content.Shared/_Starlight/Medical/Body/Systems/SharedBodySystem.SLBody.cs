// SPDX-FileCopyrightText: 2026 Starlight Network
// SPDX-License-Identifier: MIT

using Content.Shared._Starlight.Body.Components;
using Content.Shared._Starlight.Body.Events;
using Content.Shared._Starlight.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;

// ReSharper disable once CheckNamespace
namespace Content.Shared.Body.Systems;

/// <summary>
/// Bridge from the new body (<see cref="SLBodyComponent"/>) to this legacy body API while species migrate:
/// organs that are parts of an SLBody get their <see cref="OrganComponent.Body"/> and the legacy organ events,
/// and the organ queries answer from the SLBody part tree. Removed together with the legacy body.
/// </summary>
public abstract partial class SharedBodySystem
{
    [Dependency] private SLBodySystem _slBody = default!;

    private void InitializeSLBody()
    {
        SubscribeLocalEvent<OrganComponent, BodyPartEnteredBodyEvent>(OnOrganEnteredSLBody);
        SubscribeLocalEvent<OrganComponent, BodyPartLeftBodyEvent>(OnOrganLeftSLBody);
    }

    private void OnOrganEnteredSLBody(Entity<OrganComponent> organ, ref BodyPartEnteredBodyEvent args)
        => AddOrgan(organ, args.Body, args.Part.Comp.Parent);

    private void OnOrganLeftSLBody(Entity<OrganComponent> organ, ref BodyPartLeftBodyEvent args)
        => RemoveOrgan(organ, args.Part.Comp.Parent);

    /// <summary>
    /// The entity uses the new body and not the legacy one.
    /// </summary>
    private bool TryGetSLBody(EntityUid? uid, out Entity<SLBodyComponent> body)
    {
        body = default;
        if (uid is not { } id || HasComp<BodyComponent>(id) || !TryComp<SLBodyComponent>(id, out var comp))
            return false;

        body = (id, comp);
        return true;
    }

    private IEnumerable<(EntityUid Id, OrganComponent Component)> GetSLBodyOrgans(Entity<SLBodyComponent> body)
    {
        foreach (var organ in _slBody.GetBodyPartsWithComp<OrganComponent>(body))
            yield return (organ.Owner, organ.Comp2);
    }
}
