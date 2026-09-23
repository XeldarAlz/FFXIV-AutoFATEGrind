using clib.Extensions;

namespace AutoFateGrind.Core.Ipc;

internal static class DefaultCombatPreset
{
    public const string Name = AfgConstants.BundledCombatPresetName;

    public const int Revision = 2;

    private const string Base64Brotli =
        "G28SAGQ11V7fgG8j+Psne6OVE8hqb0zGZmJmiIVEt9uHvxkARx5gUaAFlgXStm3dGkt9TbV/h+HHZ9vSoL0a7Gd6mlrrSD9g" +
        "wBtBDmZV2TTG15s2cpghAyvzeqUE+QcUkpKVeWtWlSiKyhK2ltNk+q1ZVTzGEynkvz7AR5wukENjfA0Z7J6UJUAOdcDDlWb4" +
        "yj7A84mOdLKa9wHM9QXfEnz9yTT3f8WwrYZSX32xHo9J0NxV5YbK06fPKld0xTdrv4QcxoWPmm1KjVekQbMzJmp2NYvgzUjI" +
        "4QTCwgBvy46Yw11CJ40Qo9dhx5e2k2+PVqMNztBBYIMD9RD9rNfx7UsfOepWyIoygPHIS1C+kbl0v6+SBPxorB3MtiawnEB9" +
        "3LvgvupbZF04nMaATzDG4DCcaHYs9qEfAhx8OxQU83YtsFfujuLQlQDTjfLfuoKP3Yw+nmhfuMPDv/9DCCnuD0CEaWL/kd7C" +
        "N+OeZ1VpOHBaSrkdspjyriLwlWtJrxIRFqP8/cX8BpUelK8pjPLGtzCB5q4vtMnWr1xkkoOYJlWcbjlMbxSrLjEpRch/7SZr" +
        "6QivFE3P2p6lK1HnjqTWRukew0Ue77YvB4k3vFp5phsF9Z2ioqQcZmjW/PAedTlymOHrz9cX";

    private static string? cached;

    public static string GetSerialized() => cached ??= Base64Brotli.FromBase64();
}
