using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Tests.Services.Accounts;

public class AccountPermissionsTests
{
    [Theory]
    [InlineData(AccountRole.Viewer, true, false, false, false)]
    [InlineData(AccountRole.Member, true, true, false, false)]
    [InlineData(AccountRole.Admin, true, true, true, false)]
    [InlineData(AccountRole.Owner, true, true, true, true)]
    public void Matrix_GrantsExpectedCapabilities(
        AccountRole role, bool read, bool manageResources, bool manageMembers, bool manageAccount)
    {
        Assert.Equal(read, AccountPermissions.CanReadResources(role));
        Assert.Equal(manageResources, AccountPermissions.CanManageResources(role));
        Assert.Equal(manageMembers, AccountPermissions.CanManageMembers(role));
        Assert.Equal(manageAccount, AccountPermissions.CanManageAccount(role));
    }

    [Theory]
    [InlineData(AccountRole.Owner, AccountRole.Admin, true)]   // owner outranks admin
    [InlineData(AccountRole.Owner, AccountRole.Owner, false)]  // can't act on an equal
    [InlineData(AccountRole.Admin, AccountRole.Member, true)]
    [InlineData(AccountRole.Admin, AccountRole.Admin, false)]  // admin can't touch another admin
    [InlineData(AccountRole.Admin, AccountRole.Owner, false)]
    [InlineData(AccountRole.Member, AccountRole.Viewer, false)] // members can't manage members at all
    public void CanManageMember_RequiresStrictlyOutranking(AccountRole actor, AccountRole target, bool expected)
        => Assert.Equal(expected, AccountPermissions.CanManageMember(actor, target));

    [Theory]
    [InlineData(AccountRole.Owner, AccountRole.Admin, true)]
    [InlineData(AccountRole.Owner, AccountRole.Owner, false)]  // can't grant a role >= own (no second owner via invite)
    [InlineData(AccountRole.Admin, AccountRole.Member, true)]
    [InlineData(AccountRole.Admin, AccountRole.Admin, false)]  // can't grant own level
    [InlineData(AccountRole.Member, AccountRole.Viewer, false)]
    public void CanAssignRole_OnlyBelowOwnRole(AccountRole actor, AccountRole roleToAssign, bool expected)
        => Assert.Equal(expected, AccountPermissions.CanAssignRole(actor, roleToAssign));
}
