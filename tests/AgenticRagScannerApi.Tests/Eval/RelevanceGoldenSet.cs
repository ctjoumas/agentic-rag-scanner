using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Tests.Eval;

/// <summary>
/// One labeled example in the relevance golden set: a source document (identified by URL, with its
/// cleaned text) and the verdict a correct full-text relevance eval should assign for the given theme.
/// </summary>
internal sealed record GoldenItem(
    string Theme,
    string Url,
    string CleanedText,
    Verdict Expected);

/// <summary>
/// A tiny, hand-labeled golden dataset for the relevance-eval recall check (Epic 6, story 6.4). It is
/// deliberately small and deterministic - the point in Phase 6 is to stand up the recall HARNESS and
/// gate on it; the formal, CI-gated LLM eval suite over a larger curated set lands in a later epic.
/// Compliance domain: false negatives (a real update marked NOT_RELEVANT) are costlier than false
/// positives, so the harness tracks recall on the "carried" class (RELEVANT or BORDERLINE).
/// </summary>
internal static class RelevanceGoldenSet
{
    public static IReadOnlyList<GoldenItem> Items { get; } =
    [
        new("Advisory Fuel Rates",
            "https://www.gov.uk/guidance/advisory-fuel-rates",
            "HMRC advisory fuel rates from 1 September 2025: the rates you can use to reimburse employees "
            + "for business travel in company cars. These rates apply from 1 September 2025 and replace the "
            + "previous rates published in June 2025.",
            Verdict.Relevant),

        new("Advisory Fuel Rates",
            "https://www.legislation.gov.uk/uksi/2025/123/made",
            "The Income Tax (Pay As You Earn) (Amendment) Regulations 2025. These Regulations come into "
            + "force on 6 April 2026 and amend the PAYE treatment of mileage allowance payments.",
            Verdict.Relevant),

        new("Advisory Fuel Rates",
            "https://www.somecarblog.example/best-company-cars-2025",
            "Our roundup of the best company cars for 2025. We rank the top electric and hybrid models for "
            + "company car drivers, with a note on benefit-in-kind bands and advisory fuel rates.",
            Verdict.Borderline),

        new("Advisory Fuel Rates",
            "https://www.example-retailer.com/checkout",
            "Your basket is empty. Continue shopping. Sign in to your account to view saved items and track "
            + "your orders. Free delivery on orders over fifty pounds.",
            Verdict.NotRelevant),

        new("National Insurance",
            "https://www.gov.uk/government/publications/national-insurance-contributions-changes-2026",
            "National Insurance contributions: changes for the 2026 to 2027 tax year. The primary threshold "
            + "and the rates of Class 1 NICs are updated. These changes take effect from 6 April 2026.",
            Verdict.Relevant),

        new("National Insurance",
            "https://www.gov.uk/national-insurance/overview",
            "National Insurance: an overview. You pay National Insurance contributions to qualify for "
            + "certain benefits and the State Pension. Most people pay Class 1 through PAYE.",
            Verdict.Borderline),

        new("National Insurance",
            "https://www.irs.gov/credits-deductions/individuals/earned-income-tax-credit",
            "The Earned Income Tax Credit (EITC) helps low- to moderate-income workers in the United States "
            + "get a tax break. Check if you qualify and how much you can claim on your federal return.",
            Verdict.NotRelevant),

        new("National Insurance",
            "https://www.legislation.gov.uk/ukpga/2025/8/contents/enacted",
            "National Insurance Contributions Act 2025. An Act to make provision about the rates of, and "
            + "thresholds for, National Insurance contributions. Enacted 20 March 2025.",
            Verdict.Relevant),
    ];
}
