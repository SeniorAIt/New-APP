using System.ComponentModel.DataAnnotations;

namespace WorkbookManagement.Models
{
    public class Workbook1Vm
    {
        [Required, Display(Name = "Institution Name")]
        public string InstitutionName { get; set; } = string.Empty;

        [Required, Display(Name = "Institution Type")]
        public string InstitutionType { get; set; } = string.Empty;

        [Display(Name = "Registration Number")]
        public string? RegistrationNumber { get; set; }

        // Contact details
        [Display(Name = "Address")]
        public string? ContactAddress { get; set; }

        [Phone, Display(Name = "Phone")]
        public string? ContactPhone { get; set; }

        [EmailAddress, Display(Name = "Email")]
        public string? ContactEmail { get; set; }
    }
}
