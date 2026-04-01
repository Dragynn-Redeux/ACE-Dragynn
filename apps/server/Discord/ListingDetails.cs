using System.Collections.Generic;

namespace ACE.Server.Discord;

// Intermediate model used by MarketListingFormatter
internal sealed record ListingDetails
(
    string HeaderTitle,
    string SellerName,
    string ExpiresAtText,
    IReadOnlyList<string> Lines
);
