using System.Collections.ObjectModel;
using System.Windows.Media;
using AniSync.Core;

namespace AniSync.Ui;

/// <summary>Un anime proposé à la validation quand le titre détecté n'est pas reconnu avec certitude.</summary>
public sealed record SuggestionItem(int Id, string Title, string Details, ImageSource? Cover);

public sealed class MainViewModel : ObservableObject
{
    bool _listening;
    bool _isConnected;
    string? _userName;
    ImageSource? _avatar;
    bool _hasCurrent;
    string _emptyText = "";
    string _emptyHint = "";
    string _currentTitle = "";
    string _currentEpisode = "";
    string _currentSource = "";
    bool _currentPlaying;
    double _currentProgress;
    string _currentTime = "";
    bool _currentCompleted;
    ImageSource? _currentCover;
    string _currentList = "";
    string _currentListColor = "#8FA3B8";
    int _minWatchMinutes = 20;
    bool _notifications = true;
    bool _startWithWindows;
    int _ignoredCount;
    string _language = L.AutoCode;
    bool _needsRestart;
    bool _hasProfile;
    string _accountCaption = "";
    string _accountCaptionColor = "#8FA3B8";
    string _extensionStatus = L.T("Non installée", "Not installed");
    string _extensionStatusColor = "#8FA3B8";

    public MainViewModel()
    {
        History.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHistory));
        Suggestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSuggestions));
    }

    public bool Listening { get => _listening; set => Set(ref _listening, value); }
    public bool IsConnected { get => _isConnected; set => Set(ref _isConnected, value); }
    public string? UserName { get => _userName; set => Set(ref _userName, value); }
    public ImageSource? Avatar { get => _avatar; set => Set(ref _avatar, value); }

    public bool HasCurrent { get => _hasCurrent; set => Set(ref _hasCurrent, value); }
    public string EmptyText { get => _emptyText; set => Set(ref _emptyText, value); }
    public string EmptyHint { get => _emptyHint; set => Set(ref _emptyHint, value); }
    public string CurrentTitle { get => _currentTitle; set => Set(ref _currentTitle, value); }
    public string CurrentEpisode { get => _currentEpisode; set => Set(ref _currentEpisode, value); }
    public string CurrentSource { get => _currentSource; set => Set(ref _currentSource, value); }
    public bool CurrentPlaying { get => _currentPlaying; set => Set(ref _currentPlaying, value); }
    public double CurrentProgress { get => _currentProgress; set => Set(ref _currentProgress, value); }
    public string CurrentTime { get => _currentTime; set => Set(ref _currentTime, value); }
    public bool CurrentCompleted { get => _currentCompleted; set => Set(ref _currentCompleted, value); }
    public ImageSource? CurrentCover { get => _currentCover; set => Set(ref _currentCover, value); }
    public string CurrentList { get => _currentList; set => Set(ref _currentList, value); }
    public string CurrentListColor { get => _currentListColor; set => Set(ref _currentListColor, value); }

    public int MinWatchMinutes { get => _minWatchMinutes; set => Set(ref _minWatchMinutes, Math.Clamp(value, 1, 180)); }
    public bool Notifications { get => _notifications; set => Set(ref _notifications, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    public int IgnoredCount
    {
        get => _ignoredCount;
        set { if (Set(ref _ignoredCount, value)) OnPropertyChanged(nameof(HasIgnored)); }
    }

    public bool HasIgnored => IgnoredCount > 0;

    /// <summary>Langue choisie : « auto », « fr » ou « en » (appliquée au prochain démarrage).</summary>
    public string Language
    {
        get => _language;
        set
        {
            if (!Set(ref _language, value)) return;
            OnPropertyChanged(nameof(IsLanguageAuto));
            OnPropertyChanged(nameof(IsLanguageFrench));
            OnPropertyChanged(nameof(IsLanguageEnglish));
        }
    }

    public bool IsLanguageAuto { get => Language == L.AutoCode; set { if (value) Language = L.AutoCode; } }
    public bool IsLanguageFrench { get => Language == L.FrenchCode; set { if (value) Language = L.FrenchCode; } }
    public bool IsLanguageEnglish { get => Language == L.EnglishCode; set { if (value) Language = L.EnglishCode; } }

    /// <summary>La langue choisie diffère de celle affichée : il faut redémarrer.</summary>
    public bool NeedsRestart { get => _needsRestart; set => Set(ref _needsRestart, value); }

    /// <summary>Au moins un compte existe (sinon l'en-tête propose d'en ajouter un).</summary>
    public bool HasProfile { get => _hasProfile; set => Set(ref _hasProfile, value); }

    /// <summary>« AniList · connecté », « MyAnimeList · à reconnecter »...</summary>
    public string AccountCaption { get => _accountCaption; set => Set(ref _accountCaption, value); }
    public string AccountCaptionColor { get => _accountCaptionColor; set => Set(ref _accountCaptionColor, value); }

    public string ExtensionStatus { get => _extensionStatus; set => Set(ref _extensionStatus, value); }
    public string ExtensionStatusColor { get => _extensionStatusColor; set => Set(ref _extensionStatusColor, value); }

    public ObservableCollection<SuggestionItem> Suggestions { get; } = new();
    public bool HasSuggestions => Suggestions.Count > 0;

    public ObservableCollection<HistoryItem> History { get; } = new();
    public bool HasHistory => History.Count > 0;
}
