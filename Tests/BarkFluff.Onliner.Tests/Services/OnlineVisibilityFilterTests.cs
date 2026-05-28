using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Users;
using Grpc.Core;

namespace BarkFluff.Onliner.Tests.Services;

public class OnlineVisibilityFilterTests
{
    private readonly TestHelper _h = new();

    private OnlineVisibilityFilter CreateFilter()
    {
        return _h.CreateVisibilityFilter();
    }

    [Fact]
    public async Task IsVisibleToCaller_AllVisibility_ReturnsTrue()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var filter = CreateFilter();
        var result = await filter.IsVisibleToCaller(10);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsVisibleToCaller_FriendsVisibility_ReturnsFalse()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.Friends);
        var filter = CreateFilter();
        var result = await filter.IsVisibleToCaller(10);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsVisibleToCaller_NoneVisibility_ReturnsFalse()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.None);
        var filter = CreateFilter();
        var result = await filter.IsVisibleToCaller(10);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsVisibleToCaller_GrpcError_ReturnsFalse()
    {
        _h.SetupUserPrivacyError(10);
        var filter = CreateFilter();
        var result = await filter.IsVisibleToCaller(10);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsVisibleToCaller_IncrementsVisibilityChecksMetric()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var filter = CreateFilter();
        await filter.IsVisibleToCaller(10);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("visibility_checks");
    }

    [Fact]
    public async Task IsVisibleToCaller_Error_IncrementsErrorMetric()
    {
        _h.SetupUserPrivacyError(10);
        var filter = CreateFilter();
        await filter.IsVisibleToCaller(10);
        var snapshot = _h.Metrics.SnapshotAndReset();
        snapshot.Should().ContainKey("visibility_check_errors");
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_SelfAlwaysVisible()
    {
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([1], 1);
        visible.Should().Contain(1);
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_MixedVisibility()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.None);
        _h.SetupUserPrivacy(30, ProfileFieldVisibility.Friends);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([10, 20, 30], 1);
        visible.Should().Contain(10);
        visible.Should().NotContain(20);
        visible.Should().NotContain(30);
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_AllVisible_ReturnsAll()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.All);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([10, 20], 1);
        visible.Should().BeEquivalentTo([10, 20]);
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_NoneVisible_ReturnsEmpty()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.None);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.None);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([10, 20], 1);
        visible.Should().BeEmpty();
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_DeduplicatesInput()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([10, 10, 10], 1);
        visible.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_EmptyInput_ReturnsEmpty()
    {
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([], 1);
        visible.Should().BeEmpty();
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_ErrorForOneUser_FailClosed()
    {
        _h.SetupUserPrivacy(10, ProfileFieldVisibility.All);
        _h.SetupUserPrivacyError(20);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([10, 20], 1);
        visible.Should().Contain(10);
        visible.Should().NotContain(20);
    }

    [Fact]
    public async Task GetVisibleUserIdsAsync_SelfPlusOthersWithMixedVisibility()
    {
        _h.SetupUserPrivacy(1, ProfileFieldVisibility.None);
        _h.SetupUserPrivacy(20, ProfileFieldVisibility.All);
        _h.SetupUserPrivacy(30, ProfileFieldVisibility.None);
        var filter = CreateFilter();
        var visible = await filter.GetVisibleUserIdsAsync([1, 20, 30], 1);
        visible.Should().BeEquivalentTo([1, 20]);
    }
}
