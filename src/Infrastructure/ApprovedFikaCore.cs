#nullable enable
using System;
namespace PrivateStack.Infrastructure;
// Only the two exact official binaries admitted by source/IL comparison and
// the pinned scalar regression. Native acceptance still belongs to each role.
internal static class ApprovedFikaCore
{
    internal const string Relative = "BepInEx/plugins/Fika/Fika.Core.dll";
    internal const string Base = "30b2c1ac0dcaea98288f3234e29dca222064d6a910401eb9aa877d0ce1ff3d4a";
    internal const string CorpseFix = "5e62bc9c0adebd26e738dc9fd429ae24e8ecdf6f8b4ad65a2ad4ee323246395a";
    internal static bool Matches(string? relative,string? actual,string? expected)
    {
        if(actual==null||expected==null||actual.Length!=64||expected.Length!=64)return false;
        return string.Equals(actual,expected,StringComparison.Ordinal)||
            relative==Relative&&expected==Base&&(actual==Base||actual==CorpseFix);
    }
}
