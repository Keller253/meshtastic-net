using Microsoft.Extensions.Logging;
using Meshtastic.Data;
using Meshtastic.Cli.Binders;
using Meshtastic.Cli.Enums;

namespace Meshtastic.Cli.Commands;

public class RebootCommand : Command
{
    public RebootCommand(string name, string description, Option<string> port, Option<string> host, 
        Option<OutputFormat> output, Option<LogLevel> log, Option<uint?> dest, Option<bool> selectDest) : base(name, description)
    {
        var otaOption = new Option<bool>("ota", "Reboot into OTA update mode");
        otaOption.SetDefaultValue(false);

        var secondsArgument = new Argument<int>("seconds", "Number of seconds until reboot");
        secondsArgument.SetDefaultValue(5);

        this.SetHandler(async (isOtaMode, seconds, context, commandContext) =>
            {
                var handler = new RebootCommandHandler(isOtaMode, seconds, context, commandContext);
                await handler.Handle();
            },
            otaOption,
            secondsArgument,
            new DeviceConnectionBinder(port, host),
            new CommandContextBinder(log, output, dest, selectDest));
        this.AddOption(otaOption);
        this.AddArgument(secondsArgument);
    }
}
public class RebootCommandHandler : DeviceCommandHandler
{
    private bool isOtaMode = false;
    private int seconds = 5;
    public RebootCommandHandler(bool isOtaMode, 
        int seconds,
        DeviceConnectionContext context,
        CommandContext commandContext) : base(context, commandContext)
    {
        this.isOtaMode = isOtaMode;
        this.seconds = seconds;
    }
    public async Task Handle()
    {
        var wantConfig = new ToRadioMessageFactory().CreateWantConfigMessage();
        await Connection.WriteToRadio(wantConfig, CompleteOnConfigReceived);
    }

    public override async Task OnCompleted(FromDeviceMessage packet, DeviceStateContainer container)
    {
        Logger.LogInformation($"Rebooting in {seconds} seconds...");
        var adminMessageFactory = new AdminMessageFactory(container, Destination);
        var adminMessage = adminMessageFactory.CreateRebootMessage(seconds, isOtaMode);
        await Connection.WriteToRadio(ToRadioMessageFactory.CreateMeshPacketMessage(adminMessage), AnyResponseReceived);
    }
}
