using Avalonia.Threading;
using Icono.IO;

namespace Icono.Mobile.Views;

/// <summary>The drawing that is not saved yet is copied to a file of the app's private folder shortly after every change and when the app goes to the background
/// (<see cref="DocumentRecovery"/>), so that nothing is lost when Android ends the app. When the app starts and finds such a copy, it asks whether to restore it. The copy
/// is deleted as soon as the drawing is saved, or a new or another drawing is opened.</summary>
public partial class MainView
{
    /// <summary>How long after the last change the copy is written (a stroke after a stroke is one write).</summary>
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromSeconds(2);

    private static string RecoveryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-recovery.icn");

    private DispatcherTimer? _recoveryTimer;

    /// <summary>A copy found when the app started, offered once the window is there.</summary>
    private (DocumentFile File, string Name)? _recoveryOffer;

    private void SetUpRecovery()
    {
        _recoveryOffer = DocumentRecovery.TryLoad(RecoveryPath);

        _recoveryTimer = new DispatcherTimer { Interval = RecoveryDelay };
        _recoveryTimer.Tick += (_, _) => WriteRecoveryNow();
        AppLifecycle.Pausing += WriteRecoveryNow;

        Loaded += (_, _) =>
        {
            if (_recoveryOffer is not null)
            {
                _ = OfferRecoveryAsync();
            }
        };
    }

    /// <summary>The drawing changed (or was saved): a drawing with unsaved changes is copied soon, one without needs no copy.</summary>
    private void RecoveryChanged()
    {
        if (_recoveryTimer is null || _recoveryOffer is not null)
        {
            return; // the copy that was found must not be overwritten before the user has answered
        }

        _recoveryTimer.Stop();
        if (HasUnsavedChanges)
        {
            _recoveryTimer.Start();
        }
        else
        {
            DocumentRecovery.Delete(RecoveryPath);
        }
    }

    /// <summary>Writes the copy at once (the app leaves the foreground, or goes away).</summary>
    private void WriteRecoveryNow()
    {
        _recoveryTimer?.Stop();
        if (_recoveryOffer is not null)
        {
            return;
        }

        if (HasUnsavedChanges)
        {
            DocumentRecovery.Save(_context.Document, _name, RecoveryPath);
        }
    }

    /// <summary>A drawing was opened or started on purpose (the changes of the one before were saved or let go): its copy is of no use any more.</summary>
    private void ForgetRecovery()
    {
        _recoveryTimer?.Stop();
        DocumentRecovery.Delete(RecoveryPath);
    }

    private async Task OfferRecoveryAsync()
    {
        if (_recoveryOffer is not { } offer || _question is not null)
        {
            return;
        }

        QuestionTitle.Text = "Restore your drawing?";
        QuestionText.Text = $"\"{offer.Name}\" was not saved when icono was last closed. Do you want to go on with it?";
        QuestionCancelButton.IsVisible = false;
        QuestionDiscardButton.Content = "Delete it";
        QuestionSaveButton.Content = "Restore";
        QuestionLayer.IsVisible = true;
        _question = new TaskCompletionSource<QuestionAnswer>();
        var answer = await _question.Task;
        _question = null;
        QuestionLayer.IsVisible = false;
        QuestionCancelButton.IsVisible = true;
        QuestionDiscardButton.Content = "Don't save";
        QuestionSaveButton.Content = "Save";

        _recoveryOffer = null;
        if (answer == QuestionAnswer.Discard)
        {
            DocumentRecovery.Delete(RecoveryPath);
            StatusText.Text = "The unsaved drawing was deleted.";
            return;
        }

        // Restore (the Back key too: nothing is lost by it). The drawing is not saved anywhere yet, so it keeps its mark and its copy.
        LoadDocument(offer.File, null, offer.Name);
        _savedVersion = _context.UndoManager.Version - 1;
        UpdateTitle();
        StatusText.Text = $"Restored \"{offer.Name}\" - it is not saved yet: use Save to keep it.";
    }
}
