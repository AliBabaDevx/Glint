using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace GameLift;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly PowerPlanService _powerPlans = new();
    private readonly NetworkService _network = new();
    private AppSettings _settings;
    private GameProfile? _selected;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _store.Load();
        ApplyLanguage();
        RefreshProfiles();
    }

    private void SocialButton_Click(object sender, RoutedEventArgs e)
    {
       MessageBox.Show(
           "Social links will be available soon!",
           "GameLift",
           MessageBoxButton.OK,
           MessageBoxImage.Information);
    }
    
    private void RefreshProfiles()
    {
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _settings.Profiles;
        ApplyProfileFilter();
        if (_selected is not null) ProfilesList.SelectedItem = _selected;
        UpdateDashboard();
    }

    private void ProfilesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is GameProfile profile) LoadProfile(profile);
    }

    private void LoadProfile(GameProfile profile)
    {
        _selected = profile;
        NameBox.Text = profile.Name; PathBox.Text = profile.ExecutablePath;
        CategoryBox.Text = profile.Category; ArgumentsBox.Text = profile.LaunchArguments; FavoriteBox.IsChecked = profile.IsFavorite;
        ProcessesBox.Text = profile.ProcessesToClose; PowerPlanBox.IsChecked = profile.UseHighPerformancePlan;
        OverlayBox.IsChecked = profile.ShowPerformanceOverlay;
        StatusText.Text = $"Profile \"{profile.Name}\" loaded.";
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        _selected = null; ProfilesList.SelectedItem = null;
        NameBox.Clear(); PathBox.Clear(); ProcessesBox.Clear(); ArgumentsBox.Clear(); CategoryBox.Text = "General"; FavoriteBox.IsChecked = false; PowerPlanBox.IsChecked = true; OverlayBox.IsChecked = true;
        StatusText.Text = "New profile: enter details and save.";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog() == true) PathBox.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || !File.Exists(PathBox.Text))
        { StatusText.Text = _settings.Language == "en"
            ? "Please enter a name and a valid game EXE."
            : "Bitte gib einen Namen und eine gültige Spiel-EXE an."; return; }
        var profile = _selected ?? new GameProfile();
        profile.Name = NameBox.Text.Trim(); profile.ExecutablePath = PathBox.Text.Trim(); profile.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "General" : CategoryBox.Text.Trim();
        profile.LaunchArguments = ArgumentsBox.Text.Trim(); profile.IsFavorite = FavoriteBox.IsChecked == true;
        profile.ProcessesToClose = ProcessesBox.Text.Trim(); profile.UseHighPerformancePlan = PowerPlanBox.IsChecked == true;
        profile.ShowPerformanceOverlay = OverlayBox.IsChecked == true;
        if (_selected is null) _settings.Profiles.Add(profile);
        _selected = profile; _store.Save(_settings); RefreshProfiles();
        StatusText.Text = $"Profile „{profile.Name}“ saved.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _settings.Profiles.Remove(_selected); _store.Save(_settings); NewProfile_Click(sender, e); RefreshProfiles();
        StatusText.Text = "Profile deleted.";
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        Save_Click(sender, e);
        if (_selected is null || !File.Exists(_selected.ExecutablePath)) return;
        string? previousPlan = null;
        PerformanceOverlay? overlay = null;
        GameSession? session = null;
        try
        {
            CloseRequestedProcesses(_selected.ProcessesToClose);
            previousPlan = _selected.UseHighPerformancePlan ? _powerPlans.GetActiveScheme() : null;
            if (_selected.UseHighPerformancePlan) _powerPlans.SetHighPerformance();
            var game = Process.Start(new ProcessStartInfo(_selected.ExecutablePath) { UseShellExecute = true, Arguments = _selected.LaunchArguments })
                ?? throw new InvalidOperationException(
                    _settings.Language == "en"
                        ? "The game could not be started."
                        : "Das Spiel konnte nicht gestartet werden."
                );
            _selected.LaunchCount++;
            _selected.LastLaunchedUtc = DateTime.UtcNow;
            session = new GameSession { GameName = _selected.Name, StartedUtc = DateTime.UtcNow };
            _settings.Sessions.Insert(0, session);
            if (_settings.Sessions.Count > 50) _settings.Sessions.RemoveRange(50, _settings.Sessions.Count - 50);
            _store.Save(_settings);
            UpdateDashboard();
            if (_selected.ShowPerformanceOverlay) { overlay = new PerformanceOverlay(game, _selected.Name); overlay.Show(); }
            StatusText.Text = $"{_selected.Name} is running. Settings will be restored afterwards.";
            await Task.Run(() => game.WaitForExit());
            StatusText.Text = $"{_selected.Name} ended. Power plan restored.";
        }
        catch (Exception ex)
        {
            StatusText.Text = _settings.Language == "en"
                ? $"Error: {ex.Message}"
                : $"Fehler: {ex.Message}";
        }
        finally
        {
            overlay?.Close();
            if (previousPlan is not null)
            {
               try
               {
                    _powerPlans.SetScheme(previousPlan);
               }
               catch (Exception ex)
               {
                    Debug.WriteLine($"Failed to restore power plan: {ex.Message}");
               }
            }
            if (session is not null) { session.EndedUtc = DateTime.UtcNow; _store.Save(_settings); UpdateDashboard(); }
        }
    }

    private static void CloseRequestedProcesses(string rawNames)
    {
        foreach (var name in rawNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                try { if (!process.HasExited) process.CloseMainWindow(); } catch { /* protected process: ignore */ }
    }

    private void ToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        _settings.Language = _settings.Language == "en" ? "de" : "en";
        _store.Save(_settings);
        ApplyLanguage();
    }

    private async void RailButton_Click(object sender, RoutedEventArgs e)
    {
        var target = (sender as Button)?.Tag?.ToString();
        switch (target)
        {
            case "dashboard":
                SearchBox.Clear(); FavoritesOnlyBox.IsChecked = false;
                StatusText.Text = _settings.Language == "en"
                    ? "Dashboard opened."
                    : "Dashboard geöffnet.";
                break;
            case "library":
                SearchBox.Focus();
                StatusText.Text = _settings.Language == "en"
                    ? "Library: Search for a game or category."
                    : "Bibliothek: Suche einen Titel oder eine Kategorie.";
                break;
            case "network":
                await RunNetworkTestAsync();
                break;
            case "new":
                NewProfile_Click(sender, e);
                break;
            case "settings":
                var window = new SettingsWindow(_settings) { Owner = this };
                window.ShowDialog();
                ApplyLanguage();
                StatusText.Text = _settings.Language == "en"
                    ? "Settings updated."
                    : "Einstellungen aktualisiert.";
                break;
        }
    }

    private void FavoriteBox_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            StatusText.Text = _settings.Language == "en"
                ? "Save the game profile first, then you can mark it as a favorite."
                : "Speichere zuerst das neue Spielprofil, dann kannst du es als Favorit markieren.";
            return;
        }
        _selected.IsFavorite = FavoriteBox.IsChecked == true;
        _store.Save(_settings);
        RefreshProfiles();
        StatusText.Text = _selected.IsFavorite
            ? (_settings.Language == "en" ? "Added to favorites." : "Zu den Favoriten hinzugefügt.")
            : (_settings.Language == "en" ? "Removed from favorites." : "Aus den Favoriten entfernt.");
    }

    private void ApplyLanguage()
    {
        var en = _settings.Language == "en";
        LanguageButton.Content = en ? "EN" : "DE";
        SubtitleText.Text = en ? "Prepare your setup and launch focused." : "Bereite dein Setup vor und starte fokussiert.";
        NewProfileButton.Content = en ? "＋ New game" : "＋ Neues Spiel";
        ProfileTitleText.Text = en ? "Gaming Dashboard" : "Gaming Dashboard";
        NameLabel.Text = en ? "Name" : "Name"; PathLabel.Text = en ? "Game EXE" : "Spiel-EXE";
        BrowseButton.Content = en ? "Browse" : "Durchsuchen";
        PowerPlanBox.Content = en ? "High performance" : "Höchstleistung";
        OverlayBox.Content = en ? "Performance HUD" : "Performance HUD";
        ProcessesLabel.Text = en ? "Close background apps (separate with commas)" : "Hintergrundprogramme schließen (durch Komma trennen)";
        HintText.Text = en ? "GameLift only closes programs you explicitly add. Your power plan is restored after the session." : "GameLift beendet ausschließlich Programme, die du selbst einträgst. Dein Energieschema wird danach wiederhergestellt.";
        SaveButton.Content = en ? "Save" : "Speichern"; DeleteButton.Content = en ? "Delete" : "Löschen"; LaunchButton.Content = en ? "▶ Launch game" : "▶ Spiel starten";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyProfileFilter();
    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyProfileFilter();

    private void ApplyProfileFilter()
    {
        if (ProfilesList.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(ProfilesList.ItemsSource);
        var search = SearchBox?.Text?.Trim() ?? "";
        var favoritesOnly = FavoritesOnlyBox?.IsChecked == true;
        view.Filter = item => item is GameProfile p
            && (!favoritesOnly || p.IsFavorite)
            && (string.IsNullOrWhiteSpace(search) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Category.Contains(search, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }

    private void UpdateDashboard()
    {
        var launchTotal = 0;
        GameProfile? lastPlayed = null;
        foreach (var profile in _settings.Profiles)
        {
            launchTotal += profile.LaunchCount;
            if (profile.LastLaunchedUtc is not null && (lastPlayed?.LastLaunchedUtc is null || profile.LastLaunchedUtc > lastPlayed.LastLaunchedUtc)) lastPlayed = profile;
        }
        ProfileCountText.Text = $"{_settings.Profiles.Count} games";
        LaunchCountText.Text = $"{launchTotal} total";
        LastPlayedText.Text = lastPlayed?.Name ?? "None yet";
    }

    private async void NetworkTest_Click(object sender, RoutedEventArgs e) => await RunNetworkTestAsync();

    private async Task RunNetworkTestAsync()
    {
        NetworkButton.IsEnabled = false; NetworkText.Text = _settings.Language == "en"
                                            ? "Testing ..."
                                            : "Teste ...";
        try { NetworkText.Text = await _network.TestAsync(); }
        catch (Exception ex)
        {
            NetworkText.Text = $"Test failed: {ex.Message}";
        }
        finally { NetworkButton.IsEnabled = true; }
    }
}
