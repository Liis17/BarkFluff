using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Services;

namespace BarkFluff.Calls.Tests;

public class CallQualityStoreTests
{
    [Fact]
    public void GetAudio_UnknownCall_DefaultsToAuto()
    {
        var store = new CallQualityStore();

        store.GetAudio(Guid.NewGuid()).Should().Be(CallAudioQualityKind.Auto);
    }

    [Fact]
    public void SetAudio_ThenGetAudio_ReturnsStored()
    {
        var store = new CallQualityStore();
        var id = Guid.NewGuid();

        store.SetAudio(id, CallAudioQualityKind.High);

        store.GetAudio(id).Should().Be(CallAudioQualityKind.High);
    }

    [Fact]
    public void SetAudio_Overwrites_PreviousValue()
    {
        var store = new CallQualityStore();
        var id = Guid.NewGuid();

        store.SetAudio(id, CallAudioQualityKind.Low);
        store.SetAudio(id, CallAudioQualityKind.Medium);

        store.GetAudio(id).Should().Be(CallAudioQualityKind.Medium);
    }

    [Fact]
    public void Remove_ResetsToDefaultAuto()
    {
        var store = new CallQualityStore();
        var id = Guid.NewGuid();
        store.SetAudio(id, CallAudioQualityKind.High);

        store.Remove(id);

        store.GetAudio(id).Should().Be(CallAudioQualityKind.Auto);
    }

    [Fact]
    public void Remove_UnknownCall_DoesNotThrow()
    {
        var store = new CallQualityStore();

        var act = () => store.Remove(Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void State_IsIsolatedPerCall()
    {
        var store = new CallQualityStore();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        store.SetAudio(a, CallAudioQualityKind.High);

        store.GetAudio(a).Should().Be(CallAudioQualityKind.High);
        store.GetAudio(b).Should().Be(CallAudioQualityKind.Auto);
    }
}
