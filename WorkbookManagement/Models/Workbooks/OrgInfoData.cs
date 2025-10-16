using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    // Root JSON we store in WorkbookSubmission.Data for Org Info
    public class OrgInfoData
    {
        public OrgInfoSection1 Section1 { get; set; } = new(); // PART 1 (Page 3)
        public OrgInfoSection2 Section2 { get; set; } = new(); // Overview (Page 2, read-only)
        public OrgInfoSection3 Section3 { get; set; } = new(); // Qualifications / Reg status / Provinces
        public OrgInfoSection4 Section4 { get; set; } = new(); // Delivery modes
        public OrgInfoSection5 Section5 { get; set; } = new(); // Student stats (Historical)
        public OrgInfoSection6 Section6 { get; set; } = new(); // Employee stats
        public OrgInfoSection7 Section7 { get; set; } = new(); // Student stats (Current)
        public OrgInfoSection8 Section8 { get; set; } = new(); // Notes / extras

        public OrgInfoBoardSection Board { get; set; } = new();
        public OrgInfoEmploymentSection Employment { get; set; } = new();
        public OrgInfoCampusesSection Campuses { get; set; } = new();
        public OrgInfoQualificationsSection Qualifications { get; set; } = new();
        public OrgInfoPricingSection Pricing { get; set; } = new();
        public OrgInfoStudentHistoricalSection StudentHistorical { get; set; } = new();
        public OrgInfoStudentCurrentSection StudentCurrent { get; set; } = new();

        public static OrgInfoData CreateDefault() => new();
    }

    // ===== PAGE 3: PART 1 — Administrative / Head Office =====
    public class OrgInfoSection1
    {
        [Display(Name = "APPETD REG No.")] public string? AppetdRegNo { get; set; }
        [Display(Name = "YEARS REG/ACCREDITED")] public string? YearsRegAccredited { get; set; }
        [Display(Name = "LEGAL REGISTERED NAME")] public string? LegalRegisteredName { get; set; }
        [Display(Name = "TRADING NAME")] public string? TradingName { get; set; }
        [Display(Name = "COMPANY/NPC/NPO REG No.")] public string? CompanyNpcNpoRegNo { get; set; }
        [Display(Name = "BBBEE LEVEL")] public string? BBBEELevel { get; set; }

        [Display(Name = "REGISTERED/ACCREDITED SINCE")]
        [DataType(DataType.Date)]
        public DateTime? RegisteredAccreditedSince { get; set; }

        [Display(Name = "TYPE OF INSTITUTION")] public string? TypeOfInstitution { get; set; }
        [Display(Name = "other specify")] public string? OtherSpecify { get; set; }

        // Physical Address
        [Display(Name = "STREET ADDRESS 1")] public string? StreetAddress1 { get; set; }
        [Display(Name = "STREET ADDRESS 2")] public string? StreetAddress2 { get; set; }
        [Display(Name = "TOWN/CITY")] public string? TownCity { get; set; }
        [Display(Name = "PROVINCE")] public string? Province { get; set; }
        [Display(Name = "LOCAL MUNICIPALITY")] public string? LocalMunicipality { get; set; }
        [Display(Name = "POST CODE")] public string? PostCode { get; set; }
        [Display(Name = "DISTRICT MUNICIPALITY")] public string? DistrictMunicipality { get; set; }

        // Postal Address
        [Display(Name = "POSTAL ADDRESS")] public string? PostalAddress { get; set; }
        [Display(Name = "TOWN/CITY (Postal)")] public string? PostalTownCity { get; set; }
        [Display(Name = "PROVINCE (Postal)")] public string? PostalProvince { get; set; }
        [Display(Name = "POST CODE (Postal)")] public string? PostalPostCode { get; set; }

        // Contact + Web
        [Display(Name = "GENERAL CONTACT No.")] public string? GeneralContactNo { get; set; }
        [EmailAddress, Display(Name = "GENERAL EMAIL")] public string? GeneralEmail { get; set; }
        [Url, Display(Name = "WEBSITE URL")] public string? WebsiteUrl { get; set; }

        // Approvals / Accreditations
        public List<ApprovalRow> Approvals { get; set; } = new();

        // Optional items surfaced in overview if used
        public string? BoardOfDirectors { get; set; }
        public string? CampusesSites { get; set; }
    }

    public class ApprovalRow
    {
        public string Name { get; set; } = string.Empty;
        public bool IsOther { get; set; }

        public string? OtherSpecify { get; set; }

        // Leave this nullable so Step 3 status can be blank by default
        public ApprovalStatus? Status { get; set; }

        [DataType(DataType.Date)]
        public DateTime? Date { get; set; }

        // CHANGE THIS:
        public bool Future { get; set; }  // checkbox
    }

    public enum ApprovalStatus
    {
        [Display(Name = "Expired / Revoked")] Expired = 1,
        [Display(Name = "Pending")] Pending = 2,
        [Display(Name = "Current")] Current = 3
    }

    // ===== PAGE 2: Overview (no inputs) =====
    public class OrgInfoSection2 { }

    // ===== Qualifications / Reg status / Provinces (later pages) =====
    public class OrgInfoSection3
    {
        public List<QualificationItem> Qualifications { get; set; } = new();
        public List<RegistrationStatusItem> RegistrationStatuses { get; set; } = new();
        public List<ProvinceCount> ActiveProvinces { get; set; } = new();
    }
    public class QualificationItem { public string Name { get; set; } = string.Empty; public bool Offered { get; set; } public int? Quantity { get; set; } }
    public class RegistrationStatusItem { public string Name { get; set; } = string.Empty; public int? Count { get; set; } }
    public class ProvinceCount { public string Name { get; set; } = string.Empty; public int? Count { get; set; } }

    // ===== PAGE 5: Delivery modes =====
    public class OrgInfoSection4
    {
        public bool FullTimeInPerson { get; set; }
        public bool PartTimeInPerson { get; set; }
        public bool DistanceLearning { get; set; }
        public bool BlendedLearning { get; set; }
        public bool OnlineELearning { get; set; }
        public bool WorkplaceBased { get; set; }
        public bool Other { get; set; }
        public string? OtherText { get; set; }
    }

    // ===== PAGE 6: Student stats (Historical) =====
    public class OrgInfoSection5
    {
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        public int? Months { get; set; }
        public int? Enrolled { get; set; }
        public int? Male { get; set; }
        public int? Female { get; set; }
        public int? Disabled { get; set; }
        public int? SuccessfulCompletion { get; set; }
        public int? ResubmissionReassessment { get; set; }
        public int? DropOffsIncomplete { get; set; }
    }

    // ===== PAGE 7: Employee stats =====
    public class OrgInfoSection6
    {
        public List<EmployeeStatRow> EmployeeStats { get; set; } = new();
    }
    public class EmployeeStatRow
    {
        public string Group { get; set; } = string.Empty;
        public int? Employ { get; set; }
        public int? Disabled { get; set; }
        public decimal? DPercent { get; set; }
        public int? Male { get; set; }
        public decimal? MalePercent { get; set; }
        public int? MaleDisabled { get; set; }
        public decimal? MaleDisabledPercent { get; set; }
        public int? Female { get; set; }
        public decimal? FemalePercent { get; set; }
        public int? FemaleDisabled { get; set; }
        public decimal? FemaleDisabledPercent { get; set; }
    }

    // ===== PAGE 8: Student stats (Current) =====
    public class OrgInfoSection7
    {
        public string? PeriodText { get; set; }
        public int? Months { get; set; }
        public int? Enrolled { get; set; }
        public int? Male { get; set; }
        public int? Female { get; set; }
        public int? Disabled { get; set; }
        public int? InProcess { get; set; }
        public int? SuccessfulCompletion { get; set; }
        public int? ResubmissionReassessment { get; set; }
        public int? DropOffsIncomplete { get; set; }
    }

    public class OrgInfoSection8 { public string? Notes { get; set; } }

    // ===== PAGE 4: PART 2 — Board of Directors =====
    public class OrgInfoBoardSection
    {
        public int? TotalDirectors { get; set; }
        public List<DirectorRow> Directors { get; set; } = new();
    }

    public class DirectorRow
    {
        public string? Surname { get; set; }
        public string? FirstName { get; set; }
        public string? SecondName { get; set; }
        public string? Title { get; set; }
        public string? Reference { get; set; }
        public DateTime? Appointed { get; set; }
        public string? Designation { get; set; }
        public string? Gender { get; set; }
        public string? Ethnic { get; set; }

        public bool Disability { get; set; }
        public bool GovSecInterest { get; set; }
        public bool PrevEmployByGov { get; set; }
        public bool ClearCreditScore { get; set; }
        public bool ClearCriminalRecord { get; set; }
        public bool ValidQualifications { get; set; }
        public bool SuspensionFromCsd { get; set; }
        public bool JudgementsIssuedByCourt { get; set; }
    }

    public class OrgInfoEmploymentSection
    {
        public EmploymentSummary Summary { get; set; } = new();
        public List<EmploymentPosition> Positions { get; set; } = new();
    }

    public class EmploymentSummary
    {
        public RaceTotals TotalMale { get; set; } = new();
        public RaceTotals MaleDisabled { get; set; } = new();
        public RaceTotals TotalFemale { get; set; } = new();
        public RaceTotals FemaleDisabled { get; set; } = new();
    }

    public class RaceTotals
    {
        public int? African { get; set; }
        public int? Coloured { get; set; }
        public int? Indian { get; set; }
        public int? White { get; set; }
        public int? Total { get; set; }
    }

    public class EmploymentPosition
    {
        public string? PositionFunction { get; set; }
        public string? EmpType { get; set; }

        public RacialGenderCounts African { get; set; } = new();
        public RacialGenderCounts Coloured { get; set; } = new();
        public RacialGenderCounts Indian { get; set; } = new();
        public RacialGenderCounts White { get; set; } = new();
        public RacialGenderCounts Totals { get; set; } = new();
    }

    public class RacialGenderCounts
    {
        public int? M { get; set; }  // Male
        public int? MD { get; set; }  // Male (Disabled)
        public int? F { get; set; }  // Female
        public int? FD { get; set; }  // Female (Disabled)
    }

    public class OrgInfoCampusesSection
    {
        public List<CampusSiteRow> Sites { get; set; } = new();
    }

    public class CampusSiteRow
    {
        public string? SiteId { get; set; }
        public string? CampusSiteName { get; set; }

        public string? StreetAddress1 { get; set; }
        public string? StreetAddress2 { get; set; }

        public string? CityTown { get; set; }
        public string? PostalCode { get; set; }

        // Dropdown in UI (blank default + all SA provinces)
        public string? Province { get; set; }

        // Keep as string for easy capture (no locale/parsing friction). Change to double later if you prefer.
        public string? GpsLatitude { get; set; }
        public string? GpsLongitude { get; set; }

        public string? DistrictMunicipality { get; set; }
        public string? LocalMunicipality { get; set; }

        public string? ContactNo { get; set; }

        [EmailAddress]
        public string? ContactEmail { get; set; }
    }
    public class OrgInfoQualificationsSection
    {
        public List<QualificationCourseRow> Items { get; set; } = new();
    }

    public class QualificationCourseRow
    {
        public string? Code { get; set; }
        public string? Type { get; set; }

        public string? SAQAId { get; set; }
        public string? QualificationCode { get; set; }
        public string? LearnershipCode { get; set; }
        public string? OFOCode { get; set; }

        public string? Name { get; set; }
        public string? ModeOfDelivery { get; set; }
        public string? NQFLevel { get; set; }

        public int? Credits { get; set; }
        public string? SAQAFieldOfStudy { get; set; }
        public string? CESM { get; set; }

        public string? RegisteredAccreditedStatus { get; set; }

        [DataType(DataType.Date)]
        public DateTime? StatusDate { get; set; }

        public bool? SuitablyQualifiedStaff { get; set; }
        public string? StudentStaffRatio { get; set; } // e.g. "1:25"
    }
    public class OrgInfoPricingSection
    {
        public List<PricingRow> Items { get; set; } = new();
    }

    public class PricingRow
    {
        // READ-ONLY (copied from Step 7)
        public string? Code { get; set; }
        public string? QualificationName { get; set; }   // from Step7.Items[i].Name
        public string? Type { get; set; }                // from Step7.Items[i].Type
        public string? NQFLevel { get; set; }            // from Step7.Items[i].NQFLevel
        public int? Credits { get; set; }                // from Step7.Items[i].Credits

        // Editable pricing fields
        public string? DurationUnit { get; set; }        // e.g. Days / Weeks / Months
        public decimal? Duration { get; set; }           // allow .5 etc.
        public int? NotionalHours { get; set; }

        public decimal? Registration { get; set; }
        public decimal? Admin { get; set; }
        public decimal? Facilitation { get; set; }
        public decimal? TrainMaterials { get; set; }
        public decimal? PpeToolsEquipment { get; set; }
        public decimal? Overheads { get; set; }
        public decimal? Assessment { get; set; }
        public decimal? ReAssess { get; set; }
        public decimal? Moderation { get; set; }
        public decimal? Other { get; set; }
        public decimal? Certification { get; set; }

        // Calculated on the client (kept for persistence)
        public decimal? Total { get; set; }
        public decimal? AvePerNotionalHour { get; set; }
    }
    public class OrgInfoStudentHistoricalSection
    {
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        public int? Months { get; set; }

        public List<StudentHistoricalRow> Rows { get; set; } = new();
    }
    public class StudentHistoricalRow
    {
        // Dropdown selection (Programme Type)
        public string? ProgrammeType { get; set; }

        // Per-race gender breakdowns
        public GenderBreakdown African { get; set; } = new();
        public GenderBreakdown Coloured { get; set; } = new();
        public GenderBreakdown Indian { get; set; } = new();
        public GenderBreakdown White { get; set; } = new();

        // Totals & outcomes
        public int? Total { get; set; }           // sum of M+F across races
        public int? SC { get; set; }              // Successful Completion
        public decimal? SCPercent { get; set; }

        public int? PR { get; set; }              // Pending Re-Submission / Re-Assessment
        public decimal? PRPercent { get; set; }

        public int? DI { get; set; }              // Drop-offs / Incomplete
        public decimal? DIPercent { get; set; }

        public int? VAR { get; set; }             // Enrolments − (SC + PR + DI)
    }

    public class GenderBreakdown
    {
        public int? M { get; set; }  // Male
        public int? MD { get; set; }  // Male disabled
        public int? F { get; set; }  // Female
        public int? FD { get; set; }  // Female disabled
    }
    public class OrgInfoStudentCurrentSection
    {
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        public int? Months { get; set; }

        public List<StudentCurrentRow> Rows { get; set; } = new();
    }

    public class StudentCurrentRow
    {
        public string? ProgrammeType { get; set; }

        // NEW: mark a row as “completed” for snapshotting to Step9
        public bool Completed { get; set; }

        public GenderBreakdown African { get; set; } = new();
        public GenderBreakdown Coloured { get; set; } = new();
        public GenderBreakdown Indian { get; set; } = new();
        public GenderBreakdown White { get; set; } = new();

        public int? Total { get; set; }

        // Outcomes
        public int? IP { get; set; }
        public decimal? IPPercent { get; set; }

        public int? SC { get; set; }
        public decimal? SCPercent { get; set; }

        public int? PR { get; set; }
        public decimal? PRPercent { get; set; }

        public int? DI { get; set; }
        public decimal? DIPercent { get; set; }

        public int? VAR { get; set; } // Enrolments − (IP + SC + PR + DI)
    }
}
