// SPDX-FileCopyrightText: 2026 Starlight Network
// SPDX-License-Identifier: MIT

using System.Numerics;
using Content.Shared._Starlight.Body.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Body.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SLBodyComponent : Component
{
    [DataField, AutoNetworkedField] public Vector2 RootOffset = Vector2.Zero;
    [DataField, AutoNetworkedField] public ProtoId<BodyPrefabPrototype>? Prefab = null;
    [DataField, AutoNetworkedField] public bool BodyBuilt = false;
    [DataField, AutoNetworkedField] public EntityUid? RootPartEntity;

    /// <summary>
    /// Number of legs the body needs to move at full speed; movement speed is the sum of attached legs'
    /// <c>MovementBodyPart</c> speeds divided by this. 0 disables leg-based movement.
    /// </summary>
    [DataField, AutoNetworkedField] public int RequiredLegs;

    /// <summary>
    /// Cached flat list of all body parts, rebuilt from the part tree when <see cref="BodyPartsDirty"/> is set.
    /// Read it through <see cref="Systems.SLBodySystem.GetBodyParts"/>, never directly.
    /// </summary>
    [ViewVariables] internal readonly List<Entity<SLBodyPartComponent>> BodyParts = new();

    [ViewVariables] internal bool BodyPartsDirty = true;
}
