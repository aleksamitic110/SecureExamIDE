using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;

namespace Web.Api.Common.Extensions;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddOpenApiWithAuth(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.CreateSchemaReferenceId = CreateSliceQualifiedSchemaReferenceId;
        });

        return services;
    }

    // Every slice nests its own record called Request, and the default naming keys a schema by the
    // short type name alone. All of them therefore collapsed onto a single "Request" schema and the
    // first one registered won, so Scalar showed the login body on register, on create-exam, on
    // everything. Qualifying the name with the enclosing slice keeps them apart.
    private static string? CreateSliceQualifiedSchemaReferenceId(JsonTypeInfo typeInfo)
    {
        string? name = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);

        if (name is null || typeInfo.Type.DeclaringType is null)
        {
            return name;
        }

        var enclosing = new Stack<string>();

        for (Type? declaring = typeInfo.Type.DeclaringType;
             declaring is not null;
             declaring = declaring.DeclaringType)
        {
            // The Endpoint class is only where the record happens to live; naming the slice is what
            // tells a reader which body this is.
            if (declaring.Name != EndpointClassName)
            {
                enclosing.Push(declaring.Name);
            }
        }

        return string.Concat(string.Concat(enclosing), name);
    }

    private const string EndpointClassName = "Endpoint";

    internal static IServiceCollection AddCorsInternal(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy
                .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));

        return services;
    }
}
