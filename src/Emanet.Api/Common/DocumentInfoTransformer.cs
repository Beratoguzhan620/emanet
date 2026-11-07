using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Emanet.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Emanet",
            Version = "1.0",
            Description = """
                A rental marketplace for things people own and others need for a few days: a drill, a tent, a
                camera. Listings are found by words (stemmed as Turkish), category, price and distance with PostGIS.

                **Getting started**

                1. Create an account at `POST /api/auth/register` and paste the access token into *Authentication*.
                2. Rent something out at `POST /api/listings`, then add photos to it.
                3. Search at `GET /api/listings`, which needs no account.

                Exact pickup points are shown only to the owner; everyone else sees the centre of a cell a few hundred
                metres wide, and search measures distance from that cell, never from the address.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `invalid_state`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/emanet/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
