using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace WorkbookManagement.Models
{
    /// <summary>
    /// Root JSON for Workbook 2 stored in WorkbookSubmission.Data
    /// </summary>
    public class QaData
    {
        public QaOverview Overview { get; set; } = new();   // Page 1
        public QaGuide Guide { get; set; } = new();         // Page 2
        public QaSummary Summary { get; set; } = new();     // Page 3
        public QaInfo Info { get; set; } = new();           // Page 4

        public QaGEL GEL { get; set; } = new();             // Page 5
        public QaSPR SPR { get; set; } = new();             // Page 6
        public QaTLA TLA { get; set; } = new();             // Page 7
        public QaLSW LSW { get; set; } = new();             // Page 8
        public QaSCE SCE { get; set; } = new();             // Page 9
        public QaRLE RLE { get; set; } = new();             // Page 10
        public QaQMI QMI { get; set; } = new();             // Page 11
        public QaSEC SEC { get; set; } = new();             // Page 12
        public QaLCR LCR { get; set; } = new();             // Page 13

        public static QaData CreateDefault() => new();
    }

    // Page 1 — Overview
    public class QaOverview
    {
        public string? Notes { get; set; }
    }

    // Page 2 — Guide
    public class QaGuide { }

    // Page 3 — Summary
    public class QaSummary
    {
        public string? HighLevelSummary { get; set; }
    }

    // Page 4 — Info
    public class QaInfo
    {
        // Company / Institution Details
        public string? AppetdRegNo { get; set; }
        public string? TradingName { get; set; }
        public string? OrganisationType { get; set; }
        public string? SiteDepartment { get; set; }
        public string? StreetAddress1 { get; set; }
        public string? StreetAddress2 { get; set; }
        public string? Town { get; set; }
        public string? Suburb { get; set; }
        public string? Province { get; set; }
        public string? Zip { get; set; }
        public string? ContactPerson { get; set; }
        public string? ContactNumber { get; set; }
        [EmailAddress] public string? Email { get; set; }

        // Assessment panel
        public DateTime? AssessmentDate { get; set; }
        public string? OrganisationInfo { get; set; }
        public string? AccreditationStatus { get; set; }
        public string? SupportingDocsSubmitted { get; set; }
        public int? CaImplementationDeadlineDays { get; set; }
        public int? ReassessmentDeadlineDays { get; set; }
        public DateTime? CaVerificationDate { get; set; }
        public DateTime? ReassessmentDate { get; set; }

        // Assessor
        public string? AssessorFullName { get; set; }
        public string? AssessorOrganisation { get; set; }
        public string? AssessorContactNumber { get; set; }
        [EmailAddress] public string? AssessorEmail { get; set; }
    }

    // -------- Table structures shared by sections (parts + rows) --------
    public class GelPart
    {
        public string PartCode { get; set; } = "";   // e.g., "GEL 1.1", "SPR 2.1", "TLA 3.1"
        public string? Title { get; set; }           // requirement/question
        public string? Description { get; set; }     // explanatory paragraph
    }

    public class GelRow
    {
        public string PartCode { get; set; } = "";   // links to GelPart.PartCode
        public string? Requirement { get; set; }     // optional display column
        public string? Code { get; set; }            // e.g., "1.1.1", "2.1.1", "3.1.1"
        public string? Action { get; set; }          // evidence/action
        public int? CI { get; set; }                 // 3,2,1
        public string? CorrectiveAction { get; set; }
        public string? AssignedTo { get; set; }
        [DataType(DataType.Date)] public DateTime? CODate { get; set; }
        public bool CO { get; set; }
        public string? VerifiedBy { get; set; }
    }

    // Page 5 — GEL
    public class QaGEL
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 6 — SPR
    public class QaSPR
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 7 — TLA
    public class QaTLA
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 8 - LSW
    public class QaLSW
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 9 - SCE

    public class QaSCE
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 10 - RLE

    public class QaRLE
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 11 - QMI

    public class QaQMI
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 12 - SEC

    public class QaSEC
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        public int Criteria => Rows?.Count ?? 0;                   // 29
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    // Page 13 - LCR

    public class QaLCR
    {
        public string? Organisation { get; set; }
        public string? Department { get; set; }
        public string? AppetdRegNo { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
        [DataType(DataType.Date)] public DateTime? CaVerification { get; set; }
        [DataType(DataType.Date)] public DateTime? ReassessmentDate { get; set; }

        public List<GelPart> Parts { get; set; } = new();
        public List<GelRow> Rows { get; set; } = new();

        // Dashboard counters
        public int Criteria => Rows?.Count ?? 0;                   // 29
        public int Compliant => Rows?.Count(r => r.CI == 3) ?? 0;
        public int NotCompliant => Rows?.Count(r => r.CI == 2) ?? 0;
        public int NotApplicable => Rows?.Count(r => r.CI == 1) ?? 0;
        public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }


}
