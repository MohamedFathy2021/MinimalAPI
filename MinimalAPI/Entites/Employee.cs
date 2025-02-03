using System.ComponentModel.DataAnnotations;

namespace MinimalAPI.Entites
{
    public class Employee
    {
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        public string? Name { get; set; }

        public int DepartmentId { get; set; }
        public Department Department { get; set; }
    }
}
