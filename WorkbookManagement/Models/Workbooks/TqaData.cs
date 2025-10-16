using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;

namespace WorkbookManagement.Models
{
    /// <summary>Root JSON model for Workbook 3: Training Quality Assurance (TQA)</summary>
    public class TqaData
    {
        public TqaGuide Guide { get; set; } = new();
        public TqaGeneral General { get; set; } = new();

        // Part 2 – 6 (grid-based sections)
        public TqaSiteReadiness SiteReadiness { get; set; } = new();   // Part 2
        public TqaEquipment Equipment { get; set; } = new();   // Part 2 (Equipment Register)
        public TqaFacilitator Facilitator { get; set; } = new();   // Part 3
        public TqaLearner Learner { get; set; } = new();   // Part 4
        public TqaAdminSupport AdminSupport { get; set; } = new();   // Part 5
        public TqaRisk Risk { get; set; } = new();   // Part 6

        public static TqaData CreateDefault() => new()
        {
            Guide = new TqaGuide(),
            General = new TqaGeneral(),
            SiteReadiness = new TqaSiteReadiness(),
            Equipment = new TqaEquipment(),
            Facilitator = new TqaFacilitator(),
            Learner = new TqaLearner(),
            AdminSupport = new TqaAdminSupport(),
            Risk = new TqaRisk(),
        };
    }

    /// <summary>Step 1 – Guide</summary>
    public class TqaGuide
    {
        public string? Notes { get; set; }
    }

    /// <summary>Step 2 – Part 1: General Information</summary>
    public class TqaGeneral
    {
        public string? TrainingProvider { get; set; }
        public string? Site { get; set; }

        [DataType(DataType.Date)] public DateTime? StartDate { get; set; }
        [DataType(DataType.Date)] public DateTime? EndDate { get; set; }

        public string? ProgrammeName { get; set; }
        public string? ProgrammeCode { get; set; }
        public string? UnitStandard { get; set; }
        public string? NQFLevel { get; set; }
        public string? Credits { get; set; }

        // ---- Extra fields used by Step2.cshtml ----
        public string? QualificationCourseTitle { get; set; }
        public string? SaqaId { get; set; }
        public string? QctoId { get; set; }
        public string? NqfLevel { get; set; } // keep view's casing

        public string? SiteNameLocation { get; set; }
        public string? TargetLearnerGroup { get; set; }
        public string? SiteRepresentativeName { get; set; }
        public string? QaAssessorName { get; set; }
        public string? QaAssessorContactNumber { get; set; }

        public int? NumberOfLearners { get; set; } // view label: No. OF LEARNERS
        public string? Province { get; set; }

        [DataType(DataType.Date)] public DateTime? SiteAssessmentDate { get; set; }
        public string? SiteRepresentativeContactNumber { get; set; }

        public string? SetaOrAuthority { get; set; }
        public string? AccreditationNumber { get; set; }

        public string? Facilitator { get; set; }
        public string? Assessor { get; set; }
        public string? Moderator { get; set; }

        public int? LearnerCount { get; set; }
        public string? DeliveryMode { get; set; } // Classroom / Online / Blended

        public string? ContactPerson { get; set; }
        [DataType(DataType.EmailAddress)] public string? ContactEmail { get; set; }
        [DataType(DataType.PhoneNumber)] public string? ContactPhone { get; set; }

        public string? Notes { get; set; }

        // Round-trip any extra fields
        [JsonExtensionData] public Dictionary<string, object?>? Extra { get; set; }
    }

    /// <summary>Base type for grid sections (Parts + Rows)</summary>
    public abstract class TqaGridSection
    {
        public List<TqaPart> Parts { get; set; } = new();
        public List<TqaRow> Rows { get; set; } = new();

        // Generic stats for CI-based sections (e.g., Site Readiness)
        [JsonIgnore] public int Criteria => Rows.Count;
        [JsonIgnore] public int Compliant => Rows.Count(r => r.CI == 3);
        [JsonIgnore] public int NotCompliant => Rows.Count(r => r.CI == 2);
        [JsonIgnore] public int NotApplicable => Rows.Count(r => r.CI == 1);
        [JsonIgnore] public double Percent => Criteria == 0 ? 0 : (double)Compliant / Criteria;
    }

    /// <summary>Part entry for grouping rows</summary>
    public class TqaPart
    {
        public string PartCode { get; set; } = string.Empty; // e.g., "2.1"
        public string Title { get; set; } = string.Empty; // e.g., "Accessibility"
        public string? Description { get; set; }
    }

    /// <summary>Row entry for grid sections</summary>
    public class TqaRow
    {
        // Common (used by most TQA grids)
        public string PartCode { get; set; } = string.Empty; // area/group code, e.g., "2.2"
        public string Code { get; set; } = string.Empty; // requirement code, e.g., "2.2.1"
        public string Requirement { get; set; } = string.Empty; // requirement text
        public string Action { get; set; } = string.Empty; // Evidence/Action (parity with WB2)

        /// <summary>Compliance Indicator: 3 = Compliant, 2 = Not Compliant, 1 = N/A</summary>
        public int? CI { get; set; }

        public string? CorrectiveAction { get; set; }
        public string? AssignedTo { get; set; }

        [DataType(DataType.Date)] public DateTime? CODate { get; set; }
        public bool CO { get; set; } // Close Out
        public string? VerifiedBy { get; set; }

        // Equipment Register specific (optional; safe for other grids)
        public string? Item { get; set; }            // ITEM NAME/DESCRIPTION
        public string? Specification { get; set; }   // SPECIFICATION
        public string? Allocation { get; set; }      // A: F/L
        public string? RatioQty { get; set; }        // RATIO QTY (standard)
        public string? PP { get; set; }              // P/P: person/group
        public int? QtyReq { get; set; }          // QTY REQ
        public int? QtyAvail { get; set; }        // QTY AVAIL
        public int? Variance { get; set; }        // VAR (Avail - Req)
        public int? Rate { get; set; }            // CONDITION RATE (3/2/1)
    }

    /// <summary>Part 2 – Site Readiness (grid) + header strip fields</summary>
    public class TqaSiteReadiness : TqaGridSection
    {
        public string? TrainingProvider { get; set; }
        public string? Site { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
    }

    /// <summary>Part 2 – Equipment Register (grid) + header fields</summary>
    public class TqaEquipment : TqaGridSection
    {
        public string? TrainingProvider { get; set; }
        public string? QualificationTitle { get; set; }
        public int? NumberOfLearners { get; set; }
    }

    /// <summary>Part 3 – Facilitator Readiness (grid) + header strip fields</summary>
    public class TqaFacilitator : TqaGridSection
    {
        public string? TrainingProvider { get; set; }
        public string? Site { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
    }

    /// <summary>Part 4 – Learner Preparedness (grid)</summary>
    public class TqaLearner : TqaGridSection 
    {
        public string? TrainingProvider { get; set; }
        public string? Site { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
    }

    /// <summary>Part 5 – Admin & Support Systems (grid)</summary>
    public class TqaAdminSupport : TqaGridSection 
    {
        public string? TrainingProvider { get; set; }
        public string? Site { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
    }

    /// <summary>Part 6 – Risk & Contingency Planning (grid)</summary>
    public class TqaRisk : TqaGridSection
    {
        public string? TrainingProvider { get; set; }
        public string? Site { get; set; }
        [DataType(DataType.Date)] public DateTime? AssessmentDate { get; set; }
    }
}
