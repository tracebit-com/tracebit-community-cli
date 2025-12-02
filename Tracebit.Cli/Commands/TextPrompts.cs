using Spectre.Console;

namespace Tracebit.Cli.Commands;

public static class TextPrompts
{
    private const string PromptColour = "[darkgoldenrod]";
    private const string DescriptionColour = "[italic gray]";

    public static TextPrompt<string> NamePrompt(string defaultName) =>
        new TextPrompt<string>($"Type in your preferences or press Enter for default\n{PromptColour}Name[/] {DescriptionColour}– Unique identifier for this canary in Tracebit\n[/]")
            .DefaultValue(defaultName)
            .DefaultValueStyle(Style.Parse("silver"))
            .Validate(s =>
                s.Any(char.IsWhiteSpace)
                    ? ValidationResult.Error("Whitespace in name not supported")
                    : ValidationResult.Success());

    public static TextPrompt<string> ProfilePrompt(string defaultProfile) =>
        new TextPrompt<string>($"{PromptColour}AWS profile[/] {DescriptionColour}– Profile name that will appear in your AWS CLI files\n[/]")
            .DefaultValue(defaultProfile)
            .DefaultValueStyle(Style.Parse("silver"))
            .Validate(s =>
                s.Any(char.IsWhiteSpace)
                    ? ValidationResult.Error("Whitespace in profile not supported")
                    : ValidationResult.Success());

    public static TextPrompt<string> RegionPrompt(string defaultRegion) =>
        new TextPrompt<string>($"{PromptColour}AWS region[/] {DescriptionColour}– Region for the canary AWS CLI profile\n[/]")
            .DefaultValue(defaultRegion)
            .DefaultValueStyle(Style.Parse("silver"))
            .Validate(s =>
                Utils.AwsRegions().Contains(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Invalid AWS region"));

    public static TextPrompt<string> LabelsPrompt =>
        new TextPrompt<string>($"{PromptColour}Labels[/] {DescriptionColour}– Additional metadata tags for organising (e.g. 'env=home,machine=macbook')\n[/]")
            .DefaultValue("")
            .DefaultValueStyle(Style.Parse("silver"))
            .Validate(s =>
                Utils.IsLabelFormat(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error(Utils.InvalidLabelFormatMessage()));

    public static TextPrompt<string> SshKeyFileNamePrompt(string defaultSshKeyFileName) =>
        new TextPrompt<string>($"{PromptColour}SSH key file[/] {DescriptionColour}– The file name (not path) for the canary SSH key\n[/]")
            .DefaultValue(defaultSshKeyFileName)
            .DefaultValueStyle(Style.Parse("silver"))
            .Validate(s =>
                s.Any(char.IsWhiteSpace)
                    ? ValidationResult.Error("Whitespace in name not supported")
                    : ValidationResult.Success());
}
