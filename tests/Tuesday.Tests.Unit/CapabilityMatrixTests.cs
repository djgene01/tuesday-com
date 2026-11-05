using Tuesday.Application.Security;
using Tuesday.Domain.Tenancy;

namespace Tuesday.Tests.Unit;

/// <summary>
/// The capability matrix is the contract every other layer reads. If a role gains a
/// capability here, the UI and the policies change together; if it does not, a hidden
/// button and a refused request stay consistent.
/// </summary>
public sealed class CapabilityMatrixTests
{
    [Theory]
    [InlineData(TenantRole.Admin, true, true, true, true, true, true)]
    [InlineData(TenantRole.Member, true, true, true, false, false, true)]
    [InlineData(TenantRole.Viewer, true, false, false, false, false, false)]
    [InlineData(TenantRole.Guest, true, false, false, false, false, false)]
    public void Role_HasExactlyTheExpectedCapabilities(
        TenantRole role,
        bool view,
        bool create,
        bool edit,
        bool manageMembers,
        bool manageTenant,
        bool useApi)
    {
        var snapshot = PermissionSnapshot.For(role);

        Assert.Equal(view, snapshot.Can(Capability.ViewWorkspace));
        Assert.Equal(create, snapshot.Can(Capability.CreateBoard));
        Assert.Equal(edit, snapshot.Can(Capability.EditBoard));
        Assert.Equal(manageMembers, snapshot.Can(Capability.ManageMembers));
        Assert.Equal(manageTenant, snapshot.Can(Capability.ManageTenant));
        Assert.Equal(useApi, snapshot.Can(Capability.UseApi));
    }

    [Fact]
    public void Viewer_CannotObtainApiAccess()
    {
        Assert.False(PermissionSnapshot.For(TenantRole.Viewer).Can(Capability.UseApi));
        Assert.False(PermissionSnapshot.For(TenantRole.Guest).Can(Capability.UseApi));
    }

    [Theory]
    [InlineData(TenantRole.Admin, false, true)]
    [InlineData(TenantRole.Member, false, true)]
    [InlineData(TenantRole.Viewer, false, true)]
    [InlineData(TenantRole.Guest, false, false)]
    [InlineData(TenantRole.Guest, true, true)]
    public void Guest_SeesOnlyGrantedBoards(TenantRole role, bool hasGrant, bool expected)
    {
        Assert.Equal(expected, PermissionSnapshot.For(role).CanSeeBoard(hasGrant));
    }
}
