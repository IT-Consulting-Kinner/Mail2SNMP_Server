using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mail2SNMP.Tests.Infrastructure;

public class LicenseValidatorTests
{
    [Fact]
    public void NoLicenseFile_DefaultsCommunity()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        Assert.False(validator.IsEnterprise());
        Assert.Equal(LicenseEdition.Community, validator.Current.Edition);
    }

    [Fact]
    public void Community_MaxMailboxes_Is3()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        Assert.Equal(3, validator.GetLimit("maxmailboxes"));
    }

    [Fact]
    public void Community_MaxJobs_Is5()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        Assert.Equal(5, validator.GetLimit("maxjobs"));
    }

    [Fact]
    public void Community_MaxWorkerInstances_Is1()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        Assert.Equal(1, validator.GetLimit("maxworkerinstances"));
    }

    [Fact]
    public void Community_HasFeature_AlwaysFalse()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        Assert.False(validator.HasFeature("snmpv3"));
        Assert.False(validator.HasFeature("oidc"));
        Assert.False(validator.HasFeature("diagnostics"));
    }

    [Fact]
    public void GetLimit_UnknownLimit_ReturnsZero()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        Assert.Equal(0, validator.GetLimit("unknownlimit"));
    }

    [Fact]
    public async Task ReloadAsync_DoesNotThrow()
    {
        var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, "/nonexistent/license.key");
        await validator.ReloadAsync();
        Assert.Equal(LicenseEdition.Community, validator.Current.Edition);
    }
}
