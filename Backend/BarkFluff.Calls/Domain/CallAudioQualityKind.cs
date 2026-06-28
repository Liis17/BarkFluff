namespace BarkFluff.Calls.Domain;

/// <summary>Общее качество голоса звонка (применяется всеми участниками).</summary>
public enum CallAudioQualityKind
{
    /// <summary>Дефолт SDK (без явного пресета).</summary>
    Auto = 0,

    Low = 1,

    Medium = 2,

    High = 3,
}
