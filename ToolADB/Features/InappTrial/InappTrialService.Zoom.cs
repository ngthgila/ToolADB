namespace ToolAdb.Features.InappTrial
{
    internal sealed partial class InappTrialService
    {
        private string PatchZoomNoTrial(string serial)
        {
            return SwitchOfferMode(serial, "us.zoom.videomeetings", winbackMode: false);
        }
    }
}
