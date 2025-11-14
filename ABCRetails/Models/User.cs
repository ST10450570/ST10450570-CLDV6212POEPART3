using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace ABCRetails.Models
{
    
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = "Customer"; // "Customer" or "Admin"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // New properties for customer sync
        public string? TableStorageId { get; set; } // RowKey from Table Storage
        public bool IsSyncedWithTableStorage { get; set; }
        public DateTime? LastSyncDate { get; set; }

        // Customer-specific fields (for customers only)
        public string? Name { get; set; }
        public string? Surname { get; set; }
        public string? ShippingAddress { get; set; }

        // Navigation property for cart
        public virtual ICollection<Cart> CartItems { get; set; } = new List<Cart>();
    }
}