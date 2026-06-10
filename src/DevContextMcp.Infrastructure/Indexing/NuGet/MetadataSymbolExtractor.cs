using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Infrastructure.Indexing.NuGet;

internal static class MetadataSymbolExtractor
{
    public static IReadOnlyList<SymbolRecord> Extract(
        byte[] assemblyBytes,
        string assemblyPath)
    {
        using var stream = new MemoryStream(assemblyBytes, writable: false);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return [];
        }

        var reader = peReader.GetMetadataReader();
        var symbols = new List<SymbolRecord>();
        var targetFramework = GetTargetFramework(assemblyPath);
        var typeNameProvider = new MetadataTypeNameProvider();

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!IsPublic(type.Attributes))
            {
                continue;
            }

            var typeName = reader.GetString(type.Name);
            if (typeName == "<Module>" || HasCompilerGeneratedAttribute(reader, type.GetCustomAttributes()))
            {
                continue;
            }

            var typeNamespace = reader.GetString(type.Namespace);
            var fullTypeName = string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";

            symbols.Add(new(
                typeNamespace,
                fullTypeName,
                GetTypeKind(type.Attributes),
                BuildTypeSignature(type, fullTypeName),
                null,
                assemblyPath,
                targetFramework,
                $"T:{fullTypeName}"));

            AddMethods(
                reader,
                type,
                typeNamespace,
                fullTypeName,
                assemblyPath,
                targetFramework,
                typeNameProvider,
                symbols);
            AddProperties(
                reader,
                type,
                typeNamespace,
                fullTypeName,
                assemblyPath,
                targetFramework,
                typeNameProvider,
                symbols);
            AddEvents(reader, type, typeNamespace, fullTypeName, assemblyPath, targetFramework, symbols);
            AddFields(reader, type, typeNamespace, fullTypeName, assemblyPath, targetFramework, symbols);
        }

        return symbols
            .GroupBy(symbol => (
                symbol.AssemblyPath,
                symbol.Kind,
                symbol.FullyQualifiedName,
                symbol.Signature))
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddMethods(
        MetadataReader reader,
        TypeDefinition type,
        string typeNamespace,
        string fullTypeName,
        string assemblyPath,
        string? targetFramework,
        MetadataTypeNameProvider typeNameProvider,
        List<SymbolRecord> symbols)
    {
        foreach (var handle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            var metadataName = reader.GetString(method.Name);
            var isConstructor = metadataName == ".ctor";
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || ((method.Attributes & MethodAttributes.SpecialName) != 0 && !isConstructor)
                || HasCompilerGeneratedAttribute(reader, method.GetCustomAttributes()))
            {
                continue;
            }

            var signature = method.DecodeSignature(typeNameProvider, genericContext: null);
            var parameterNames = method.GetParameters()
                .Select(parameterHandle => reader.GetParameter(parameterHandle))
                .Where(parameter => parameter.SequenceNumber > 0)
                .OrderBy(parameter => parameter.SequenceNumber)
                .Select(parameter => reader.GetString(parameter.Name))
                .Select(parameterName => string.IsNullOrEmpty(parameterName) ? "arg" : parameterName)
                .ToArray();
            var parameters = signature.ParameterTypes
                .Select((typeName, index) =>
                    $"{typeName} {(index < parameterNames.Length ? parameterNames[index] : $"arg{index}")}")
                .ToArray();
            var name = isConstructor ? fullTypeName.Split('.').Last() : metadataName;
            var memberName = $"{fullTypeName}.{name}";
            var staticModifier = (method.Attributes & MethodAttributes.Static) != 0
                ? "static "
                : string.Empty;
            var returnType = isConstructor ? string.Empty : $"{signature.ReturnType} ";

            symbols.Add(new(
                typeNamespace,
                memberName,
                isConstructor ? "constructor" : "method",
                $"public {staticModifier}{returnType}{name}({string.Join(", ", parameters)})",
                fullTypeName,
                assemblyPath,
                targetFramework,
                isConstructor ? $"M:{fullTypeName}.#ctor" : $"M:{memberName}"));
        }
    }

    private static void AddProperties(
        MetadataReader reader,
        TypeDefinition type,
        string typeNamespace,
        string fullTypeName,
        string assemblyPath,
        string? targetFramework,
        MetadataTypeNameProvider typeNameProvider,
        List<SymbolRecord> symbols)
    {
        foreach (var handle in type.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            var accessors = property.GetAccessors();
            if (!IsPublicAccessor(reader, accessors.Getter)
                && !IsPublicAccessor(reader, accessors.Setter))
            {
                continue;
            }

            var name = reader.GetString(property.Name);
            var memberName = $"{fullTypeName}.{name}";
            var signature = property.DecodeSignature(typeNameProvider, genericContext: null);
            symbols.Add(new(
                typeNamespace,
                memberName,
                "property",
                $"public {signature.ReturnType} {name} {{ {(accessors.Getter.IsNil ? string.Empty : "get; ")}{(accessors.Setter.IsNil ? string.Empty : "set; ")} }}",
                fullTypeName,
                assemblyPath,
                targetFramework,
                $"P:{memberName}"));
        }
    }

    private static void AddEvents(
        MetadataReader reader,
        TypeDefinition type,
        string typeNamespace,
        string fullTypeName,
        string assemblyPath,
        string? targetFramework,
        List<SymbolRecord> symbols)
    {
        foreach (var handle in type.GetEvents())
        {
            var eventDefinition = reader.GetEventDefinition(handle);
            var accessors = eventDefinition.GetAccessors();
            if (!IsPublicAccessor(reader, accessors.Adder))
            {
                continue;
            }

            var name = reader.GetString(eventDefinition.Name);
            var memberName = $"{fullTypeName}.{name}";
            symbols.Add(new(
                typeNamespace,
                memberName,
                "event",
                $"event {name}",
                fullTypeName,
                assemblyPath,
                targetFramework,
                $"E:{memberName}"));
        }
    }

    private static void AddFields(
        MetadataReader reader,
        TypeDefinition type,
        string typeNamespace,
        string fullTypeName,
        string assemblyPath,
        string? targetFramework,
        List<SymbolRecord> symbols)
    {
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public
                || (field.Attributes & FieldAttributes.SpecialName) != 0)
            {
                continue;
            }

            var name = reader.GetString(field.Name);
            var memberName = $"{fullTypeName}.{name}";
            var signature = field.DecodeSignature(new MetadataTypeNameProvider(), genericContext: null);
            symbols.Add(new(
                typeNamespace,
                memberName,
                "field",
                $"public {signature} {name}",
                fullTypeName,
                assemblyPath,
                targetFramework,
                $"F:{memberName}"));
        }
    }

    private static bool IsPublic(TypeAttributes attributes)
    {
        var visibility = attributes & TypeAttributes.VisibilityMask;
        return visibility is TypeAttributes.Public or TypeAttributes.NestedPublic;
    }

    private static bool IsPublicAccessor(MetadataReader reader, MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
        {
            return false;
        }

        var method = reader.GetMethodDefinition(handle);
        return (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
    }

    private static bool HasCompilerGeneratedAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            var constructor = attribute.Constructor;
            StringHandle nameHandle;
            StringHandle namespaceHandle;

            if (constructor.Kind == HandleKind.MemberReference)
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)constructor);
                if (member.Parent.Kind != HandleKind.TypeReference)
                {
                    continue;
                }

                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                nameHandle = type.Name;
                namespaceHandle = type.Namespace;
            }
            else
            {
                continue;
            }

            if (reader.GetString(nameHandle) == "CompilerGeneratedAttribute"
                && reader.GetString(namespaceHandle) == "System.Runtime.CompilerServices")
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTypeKind(TypeAttributes attributes)
    {
        if ((attributes & TypeAttributes.Interface) != 0)
        {
            return "interface";
        }

        return "type";
    }

    private static string BuildTypeSignature(TypeDefinition type, string fullTypeName)
    {
        var kind = (type.Attributes & TypeAttributes.Interface) != 0
            ? "interface"
            : "type";
        return $"public {kind} {fullTypeName}";
    }

    private static string? GetTargetFramework(string path)
    {
        var segments = path.Replace('\\', '/').Split('/');
        return segments.Length >= 3 && (segments[0] == "lib" || segments[0] == "ref")
            ? segments[1]
            : null;
    }
}
