using Tracebit.Cli.Deploy;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Tests.Deploy;

public class DeploymentInputValidatorTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.255.1")]
    [InlineData("2001:db8::1")]
    public void ValidateSshIp_LiteralAddress_Passes(string ip) =>
        DeploymentInputValidator.ValidateSshIp(ip);

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("10.1.2.3\nMatch exec \"touch /tmp/pwned\"")]
    [InlineData("10.1.2.3\r\n  ProxyCommand evil")]
    [InlineData("10.1.2.3 10.1.2.4")]
    [InlineData("10.1.2.3:22")]
    [InlineData("10.1")]
    [InlineData("0x0A.1.2.3")]
    [InlineData("fe80::1%eth0")]
    [InlineData("*")]
    public void ValidateSshIp_NotALiteralAddress_Throws(string ip) =>
        Assert.Throws<InvalidDeploymentInputException>(() => DeploymentInputValidator.ValidateSshIp(ip));

    [Theory]
    [InlineData("canary-key")]
    [InlineData("id_tracebit.key")]
    public void ValidateSshKeyFileName_PlainFileName_Passes(string name) =>
        DeploymentInputValidator.ValidateSshKeyFileName(name);

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../.bashrc")]
    [InlineData("..\\evil")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/key")]
    [InlineData("my key")]
    [InlineData("key\nIdentityFile /tmp/x")]
    [InlineData("config")]
    [InlineData("authorized_keys")]
    [InlineData("authorized_keys2")]
    [InlineData("known_hosts")]
    [InlineData("known_hosts2")]
    [InlineData("rc")]
    [InlineData("environment")]
    [InlineData("Authorized_Keys")]
    public void ValidateSshKeyFileName_PathOrInjection_Throws(string name) =>
        Assert.Throws<InvalidDeploymentInputException>(() => DeploymentInputValidator.ValidateSshKeyFileName(name));

    [Theory]
    [InlineData("canary")]
    [InlineData("tracebit-canary_1")]
    [InlineData("user@example.com")]
    public void ValidateAwsProfile_Allowed_Passes(string profile) =>
        DeploymentInputValidator.ValidateAwsProfile(profile);

    [Theory]
    [InlineData("")]
    [InlineData("profile")]
    [InlineData("PROFILE")]
    [InlineData("my profile")]
    [InlineData("canary]\n[default")]
    [InlineData("canary]\nregion = x\n[other")]
    [InlineData("canary;comment")]
    [InlineData("canary=1")]
    [InlineData("canary\n")]
    public void ValidateAwsProfile_Invalid_Throws(string profile) =>
        Assert.Throws<InvalidDeploymentInputException>(() => DeploymentInputValidator.ValidateAwsProfile(profile));

    [Theory]
    [InlineData("us-east-1")]
    [InlineData("eu-west-2")]
    [InlineData("ap-southeast-10")]
    [InlineData("us-gov-west-1")]
    [InlineData("cn-north-1")]
    [InlineData("us-iso-east-1")]
    [InlineData("us-isob-east-1")]
    public void ValidateAwsRegion_RealShape_Passes(string region) =>
        DeploymentInputValidator.ValidateAwsRegion(region);

    [Theory]
    [InlineData("")]
    [InlineData("US-EAST-1")]
    [InlineData("us-east-1\ncredential_process = /tmp/evil")]
    [InlineData("us-east-1\n[profile other]")]
    [InlineData("us-east")]
    [InlineData("us east 1")]
    [InlineData("us-east-1\n")]
    public void ValidateAwsRegion_Invalid_Throws(string region) =>
        Assert.Throws<InvalidDeploymentInputException>(() => DeploymentInputValidator.ValidateAwsRegion(region));

    [Fact]
    public void ValidateAws_IssuedValuesClean_Passes() =>
        DeploymentInputValidator.ValidateAws(TestDataBuilder.BuildAwsCredentials(), "canary", "eu-west-1");

    [Theory]
    [InlineData("AKIA\n[default]", "secret", "session")]
    [InlineData("AKIA", "secret\ncredential_process = /tmp/evil", "session")]
    [InlineData("AKIA", "secret", "session\n[profile other]\nregion = us-east-1")]
    [InlineData("AKIA", "secret", "")]
    [InlineData("AKIA", "sec ret", "session")]
    [InlineData("AKIA", "secret", "session;comment")]
    [InlineData("AKIA", "secret", "session\n")]
    public void ValidateAws_IssuedValueWithIniMetacharacters_Throws(string keyId, string secret, string token)
    {
        var credentials = TestDataBuilder.BuildAwsCredentials(accessKeyId: keyId, secretAccessKey: secret, sessionToken: token);
        Assert.Throws<InvalidDeploymentInputException>(() => DeploymentInputValidator.ValidateAws(credentials, "canary", "eu-west-1"));
    }

    [Fact]
    public void ValidateSsh_IssuedValuesClean_Passes() =>
        DeploymentInputValidator.ValidateSsh(TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3"), "canary-key");

    [Theory]
    [InlineData("not base64!", "cHVibGlj")]
    [InlineData("cHJpdmF0ZQ==", "")]
    public void ValidateSsh_KeyMaterialNotBase64_Throws(string privateKey, string publicKey)
    {
        var credentials = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: privateKey, publicKey: publicKey);
        Assert.Throws<InvalidDeploymentInputException>(() => DeploymentInputValidator.ValidateSsh(credentials, "canary-key"));
    }
}
