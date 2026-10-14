// SPDX-FileCopyrightText: 2026 Starlight Network
// SPDX-License-Identifier: MIT

#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Starlight.Body.Components;
using Content.Shared._Starlight.Body.Prototypes;
using Content.Shared._Starlight.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._Starlight.Body;

/// <summary>
/// Every body prefab builds into a complete part tree without errors.
/// </summary>
[TestFixture]
[TestOf(typeof(BodyPrefabPrototype))]
public sealed class BodyPrefabBuildTest : GameTest
{
    [Test]
    public async Task AllPrefabsBuild()
    {
        var server = Pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var bodySys = entMan.System<SLBodySystem>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var prefab in protoMan.EnumeratePrototypes<BodyPrefabPrototype>())
            {
                var uid = entMan.Spawn(null, map.MapCoords);
                var body = new Entity<SLBodyComponent>(uid, entMan.AddComponent<SLBodyComponent>(uid));
                body.Comp.Prefab = prefab.ID;
                Assert.That(bodySys.BuildBody(body), $"{prefab.ID} did not build");
                Assert.That(bodySys.GetBodyParts(body), Has.Count.EqualTo(CountParts(prefab.Root)),
                    $"{prefab.ID}: part count does not match the prefab");
                entMan.DeleteEntity(uid);
            }
        });
    }

    private static int CountParts(BodyPartDef def)
    {
        var count = 1;
        foreach (var children in new[] { def.AttachedParts, def.ContainedParts })
        {
            if (children == null)
                continue;
            foreach (var child in children.Values)
                count += CountParts(child);
        }

        return count;
    }
}
