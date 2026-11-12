using Kokishin.Domain.Profiles;

namespace Kokishin.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void Profile_ValidFields_AreNormalized()
    {
        var profile = new Profile(Guid.NewGuid(), "  Ada Lovelace  ", "  Instructor  ");

        Assert.Equal("Ada Lovelace", profile.DisplayName);
        Assert.Equal("Instructor", profile.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Profile_EmptyDisplayName_IsRejected(string displayName)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Profile(Guid.NewGuid(), displayName));

        Assert.Equal("displayName", exception.ParamName);
    }

    [Fact]
    public void Profile_DisplayNameExceedsMaximumLength_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Profile(Guid.NewGuid(), new string('a', Profile.MaxDisplayNameLength + 1)));

        Assert.Equal("displayName", exception.ParamName);
    }

    [Fact]
    public void Profile_DescriptionExceedsMaximumLength_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Profile(Guid.NewGuid(), "Ada", new string('a', Profile.MaxDescriptionLength + 1)));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Profile_Update_ChangesEditableFieldsButNotAccountId()
    {
        var accountId = Guid.NewGuid();
        var profile = new Profile(accountId, "Ada", "Teacher");

        profile.Update("Grace", null);

        Assert.Equal(accountId, profile.AccountId);
        Assert.Equal("Grace", profile.DisplayName);
        Assert.Null(profile.Description);
    }

    [Fact]
    public void Profile_WhitespaceDescription_IsTreatedAsAbsent()
    {
        var profile = new Profile(Guid.NewGuid(), "Ada", " ");

        Assert.Null(profile.Description);
    }
}
