using RtiTypeKind = Omg.Types.Dynamic.TypeKind;

namespace DdsScope.Dds.Rti;

/// <summary>
/// TypeKind members that exist on some Connext releases but not others.
///
/// The 7.3.0 baseline has no Int8 and no Octet; 7.7 on the prototype machine has both.
/// Naming them in a switch would make the adapter build on one release only, so they are
/// resolved by name once at startup and compared as values instead.
///
/// Adding another release-dependent kind is one line here plus a comparison at the use site.
/// </summary>
internal static class RtiKindCompat
{
    /// <summary>Signed 8-bit IDL type. Null on releases that do not define it.</summary>
    public static readonly RtiTypeKind? Int8 = Resolve("Int8");

    /// <summary>
    /// The octet alias. Null on 7.3.0, where an IDL octet is reported as Uint8 and is
    /// already handled by the ordinary switch.
    /// </summary>
    public static readonly RtiTypeKind? Octet = Resolve("Octet");

    private static RtiTypeKind? Resolve(string name) =>
        Enum.TryParse<RtiTypeKind>(name, ignoreCase: false, out var kind) && Enum.IsDefined(typeof(RtiTypeKind), kind)
            ? kind
            : null;
}
