using System.Diagnostics;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class NewVisionProcessingService
{
    public static bool IsIncludedByPackage => RAM.SettingModel.NewVisionProcessingIncluded || Debugger.IsAttached;
    public bool IsEnabled => IsIncludedByPackage && RAM.SettingModel.NewVisionProcessingEnabled;
    public NewVisionProcessingService()
    {
        if (IsIncludedByPackage && !RAM.SettingModel.NewVisionProcessingEnabled)
        {
            RAM.SettingModel.NewVisionProcessingEnabled = true;
        }
    }
    public void SetEnabled(bool enabled)
    {
        if (IsIncludedByPackage) RAM.SettingModel.NewVisionProcessingEnabled = enabled;
    }
}
