// SPDX-FileCopyrightText: 2026 Starlight Network
// SPDX-License-Identifier: MIT

using Content.Shared._Starlight.Body.Editor;
using Content.Shared.Preferences;

namespace Content.Client._Starlight.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private const int BodyTabIndex = 5;

    public event Action<bool>? BodyTabVisibilityChanged;

    private void InitializeBodyTab()
    {
        BodyTab.Initialize(_prototypeManager, _sprite);
        BodyTab.BodyProfileChanged += OnBodyProfileChanged;
        BodyTab.ProfileChanged += OnBodyTabProfileChanged;

        TabContainer.OnTabChanged += UpdatePreviewVisibility;
        UpdatePreviewVisibility(TabContainer.CurrentTab);
    }

    private void UpdatePreviewVisibility(int tabIndex)
    {
        var bodyTabVisible = tabIndex == BodyTabIndex;
        PreviewPanel.Visible = !bodyTabVisible;
        BodyTabVisibilityChanged?.Invoke(bodyTabVisible);
    }

    private void OnBodyProfileChanged(BodyProfile profile)
    {
        if (Profile is null)
            return;

        // Persist the hierarchical body editor profile into the character profile so it is sent on save.
        Profile = Profile.WithBodyEditorProfile(profile);
        SetDirty();
    }

    private void OnBodyTabProfileChanged(HumanoidCharacterProfile profile)
    {
        Profile = profile;
        ReloadPreview();
        SetDirty();
    }
}
