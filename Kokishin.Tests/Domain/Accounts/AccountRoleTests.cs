using Kokishin.Domain.Accounts;

namespace Kokishin.Tests;

public sealed class AccountRoleTests
{
    [Theory]
    [InlineData(AccountRole.Instructor, 1, "Instructor")]
    [InlineData(AccountRole.Learner, 2, "Learner")]
    public void AccountRole_InitialValues_AreStable(AccountRole role, int numericValue, string name)
    {
        Assert.Equal(numericValue, (int)role);
        Assert.Equal(name, role.ToString());
    }

    [Fact]
    public void Account_UnsupportedRole_IsRejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Account("https://identity.example.test/", "subject-1", (AccountRole)0));

        Assert.Equal("role", exception.ParamName);
    }
}
