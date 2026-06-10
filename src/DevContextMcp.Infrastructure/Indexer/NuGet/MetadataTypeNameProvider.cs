using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DevContextMcp.Infrastructure.Indexer.NuGet;

internal sealed class MetadataTypeNameProvider :
    ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) =>
        $"{elementType}[{new string(',', Math.Max(0, shape.Rank - 1))}]";

    public string GetByReferenceType(string elementType) => $"ref {elementType}";

    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        $"delegate*<{string.Join(", ", signature.ParameterTypes.Append(signature.ReturnType))}>";

    public string GetGenericInstantiation(
        string genericType,
        ImmutableArray<string> typeArguments) =>
        $"{TrimGenericArity(genericType)}<{string.Join(", ", typeArguments)}>";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"M{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"T{index}";

    public string GetModifiedType(
        string modifier,
        string unmodifiedType,
        bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.UIntPtr => "nuint",
            PrimitiveTypeCode.Void => "void",
            _ => typeCode.ToString()
        };

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        return GetFullName(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return GetFullName(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static string GetFullName(string typeNamespace, string name)
    {
        var typeName = TrimGenericArity(name);
        return string.IsNullOrEmpty(typeNamespace)
            ? typeName
            : $"{typeNamespace}.{typeName}";
    }

    private static string TrimGenericArity(string name)
    {
        var delimiter = name.IndexOf('`', StringComparison.Ordinal);
        return delimiter < 0 ? name : name[..delimiter];
    }
}
