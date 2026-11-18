namespace Emanet.Api.Features.Demo;

/// <summary>
/// The public demo: neighbours in Samsun renting things out, with photos, reviews and a request waiting for an
/// answer, and three sign-ins it publishes (a renter, an owner and support). Off everywhere else.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Where this API listens, for the demo's own calls.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>The flat drawings uploaded as the listings' photos.</summary>
    public string PhotosPath { get; set; } = "/app/demo-photos";

    /// <summary>How often the owner is given a new request to answer, when the last one has been answered.</summary>
    public TimeSpan Every { get; set; } = TimeSpan.FromHours(1);

    public string RenterEmail { get; set; } = "ayse@emanet.demo";

    public string OwnerEmail { get; set; } = "mehmet@emanet.demo";

    /// <summary>Support's sign-in; it must also be in Admin:Emails to see claims and the books.</summary>
    public string SupportEmail { get; set; } = "support@emanet.demo";

    /// <summary>Shared by every demo account; published on the sign-in page, so it guards nothing.</summary>
    public string Password { get; set; } = "emanet-demo-password";
}
