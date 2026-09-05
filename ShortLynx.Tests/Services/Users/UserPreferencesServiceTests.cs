using ShortLynx.Data.Enums;
using ShortLynx.Services.Users;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Users;

public class UserPreferencesServiceTests
{
    [Fact]
    public async Task GetNavStyleAsync_FreshUser_DefaultsToHamburger()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount();
        await using (var seed = db.CreateContext()) { seed.Add(user); await seed.SaveChangesAsync(); }

        await using var ctx = db.CreateContext();
        var svc = new UserPreferencesService(ctx);

        Assert.Equal(NavStyle.Hamburger, await svc.GetNavStyleAsync(user.Id));
    }

    [Fact]
    public async Task SetNavStyleAsync_PersistsAcrossContexts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = EntityFactory.UserAccount();
        await using (var seed = db.CreateContext()) { seed.Add(user); await seed.SaveChangesAsync(); }

        await using (var ctx = db.CreateContext())
        {
            var svc = new UserPreferencesService(ctx);
            Assert.True(await svc.SetNavStyleAsync(user.Id, NavStyle.HorizontalScroll));
        }

        await using var verify = db.CreateContext();
        var verifySvc = new UserPreferencesService(verify);
        Assert.Equal(NavStyle.HorizontalScroll, await verifySvc.GetNavStyleAsync(user.Id));
    }

    [Fact]
    public async Task GetAndSet_NonexistentUser_ReturnNullAndFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var svc = new UserPreferencesService(ctx);
        var missingId = Guid.CreateVersion7();

        Assert.Null(await svc.GetNavStyleAsync(missingId));
        Assert.False(await svc.SetNavStyleAsync(missingId, NavStyle.HorizontalScroll));
    }
}
