using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidgardStudio.App.Localization;
using Wpf.Ui.Controls;

namespace MidgardStudio.App.ViewModels;

/// <summary>One slide in the first-run onboarding tour.</summary>
public sealed partial class OnboardingPage : ObservableObject
{
    public OnboardingPage(SymbolRegular icon, string title, string body)
    {
        Icon = icon;
        Title = title;
        Body = body;
    }

    public SymbolRegular Icon { get; }
    public string Title { get; }
    public string Body { get; }

    /// <summary>True when this page is the one currently shown (drives the progress dots).</summary>
    [ObservableProperty] private bool _isActive;
}

/// <summary>
/// First-run onboarding: a short, animated feature tour shown exactly once, before the configuration window.
/// "Skip" and the final "Get started" both raise <see cref="Completed"/>, which the shell uses to record that
/// onboarding has been seen and dismiss the overlay.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    public OnboardingViewModel()
    {
        Pages = new[]
        {
            new OnboardingPage(SymbolRegular.Sparkle24, L("Onboard_P1_Title"), L("Onboard_P1_Body")),
            new OnboardingPage(SymbolRegular.Database24, L("Onboard_P2_Title"), L("Onboard_P2_Body")),
            new OnboardingPage(SymbolRegular.Image24, L("Onboard_P3_Title"), L("Onboard_P3_Body")),
            new OnboardingPage(SymbolRegular.Wand24, L("Onboard_P4_Title"), L("Onboard_P4_Body")),
            new OnboardingPage(SymbolRegular.ShieldCheckmark24, L("Onboard_P5_Title"), L("Onboard_P5_Body")),
        };
        Pages[0].IsActive = true;
    }

    /// <summary>Raised when the user finishes or skips the tour.</summary>
    public event Action? Completed;

    public IReadOnlyList<OnboardingPage> Pages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    [NotifyPropertyChangedFor(nameof(IsFirst))]
    [NotifyPropertyChangedFor(nameof(IsLast))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    private int _currentIndex;

    public OnboardingPage CurrentPage => Pages[CurrentIndex];
    public bool IsFirst => CurrentIndex == 0;
    public bool IsLast => CurrentIndex == Pages.Count - 1;
    public string StepLabel => $"{CurrentIndex + 1} / {Pages.Count}";

    partial void OnCurrentIndexChanged(int oldValue, int newValue)
    {
        if (oldValue >= 0 && oldValue < Pages.Count) Pages[oldValue].IsActive = false;
        if (newValue >= 0 && newValue < Pages.Count) Pages[newValue].IsActive = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Finish();
        else CurrentIndex++;
    }

    [RelayCommand]
    private void Back()
    {
        if (!IsFirst) CurrentIndex--;
    }

    [RelayCommand]
    private void Skip() => Finish();

    [RelayCommand]
    private void Finish() => Completed?.Invoke();

    /// <summary>Shorthand for a localized string read from the active language dictionary.</summary>
    private static string L(string key) => LocalizationService.Get(key);
}
