using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace NetShareDoctor.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum CheckStatus { Ok, Warning, Problem, Info }

public sealed class DiagnosticCheck : ObservableObject
{
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public CheckStatus Status { get; init; }

    /// <summary>Button text for the fix, e.g. "Make Private".</summary>
    public string? FixLabel { get; init; }
    /// <summary>PowerShell run as administrator when the fix is clicked.</summary>
    public string? FixScript { get; init; }
    public string? FixTooltip => FixScript == null ? null : "Runs as administrator:\n" + FixScript;
    /// <summary>The fix lowers security, so ask before running it.</summary>
    public bool FixNeedsConfirm { get; init; }

    private ICommand? _fixCommand;
    public ICommand? FixCommand { get => _fixCommand; set { if (Set(ref _fixCommand, value)) Raise(nameof(HasFix)); } }
    public bool HasFix => FixScript != null && FixCommand != null;

    public bool NeedsAttention => Status is CheckStatus.Problem or CheckStatus.Warning;

    public string StatusText => Status switch
    {
        CheckStatus.Ok => "OK",
        CheckStatus.Warning => "CHECK",
        CheckStatus.Problem => "PROBLEM",
        _ => "INFO",
    };

    public override string ToString() =>
        $"[{StatusText}] {Category}: {Title}" + (string.IsNullOrWhiteSpace(Detail) ? "" : Environment.NewLine + "        " + Detail.Replace("\n", "\n        "));
}

public sealed class SavedConnection : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _name = "New connection";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string _host = "";
    public string Host { get => _host; set { if (Set(ref _host, value)) Raise(nameof(UncPath)); } }

    private string _share = "";
    public string Share { get => _share; set { if (Set(ref _share, value)) Raise(nameof(UncPath)); } }

    private string _user = "";
    public string UserName { get => _user; set => Set(ref _user, value); }

    private string _drive = "None";
    public string DriveLetter { get => _drive; set => Set(ref _drive, value ?? "None"); }

    private bool _reconnect = true;
    public bool Reconnect { get => _reconnect; set => Set(ref _reconnect, value); }

    private bool _savePassword = true;
    public bool SavePassword { get => _savePassword; set => Set(ref _savePassword, value); }

    private string _notes = "";
    public string Notes { get => _notes; set => Set(ref _notes, value); }

    [JsonIgnore]
    public string UncPath => string.IsNullOrWhiteSpace(Host) ? "(no PC set)"
        : string.IsNullOrWhiteSpace(Share) ? $@"\\{Host}" : $@"\\{Host}\{Share}";

    [JsonIgnore]
    public bool HasDrive => DriveLetter is { Length: 2 } && DriveLetter[1] == ':';
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _run;
    private bool _busy;

    public RelayCommand(Func<Task> run) => _run = run;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy;

    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _run(); }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record ShareRow(string Name, string Path, string Description, string Access, string NetworkPath);

public sealed record DriveRow(string Local, string Remote, string Status);

public sealed record ToolCommand(string Category, string Name, string Description, string Script, bool Admin = false)
{
    public string Label => Admin ? Name + "  (admin)" : Name;
}
