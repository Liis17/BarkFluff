using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class ChatItemViewModel
{
    private bool _serverHasDraft;
    private ChatDraft? _localDraft;

    public bool HasListPreview => !string.IsNullOrEmpty(ListPreview);

    private void NotifyDraftPreviewChanged()
    {
        OnPropertyChanged(nameof(ListPreview));
        OnPropertyChanged(nameof(HasListPreview));
    }

    public string ListPreview
    {
        get
        {
            var hasDraft = _localDraft?.HasContent ?? _serverHasDraft;
            if (!hasDraft)
                return Preview;

            var label = _localization.GetString("Messenger_Draft");
            var text = _localDraft?.Text;
            return string.IsNullOrWhiteSpace(text) ? label : $"{label}: {CreatePreview(text, 20)}";
        }
    }

    internal void SetDraftPreview(ChatDraft? draft)
    {
        if (ReferenceEquals(_localDraft, draft))
            return;

        var previous = ListPreview;
        _localDraft = draft;
        if (previous != ListPreview)
            NotifyDraftPreviewChanged();
    }
}
