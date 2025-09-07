using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class RoomOccupancyDto
    {
        [Required]
        public int FMemberId { get; set; } // 新增會員 ID
        [Required]
        public int FRoomId { get; set; }
        public int FOccupancyId { get; set; }
        [Required]
        public int FBedId { get; set; }
        [Required]
        public DateTime FCheckInDate { get; set; }

        [Required]
        public int FBillingAmount { get; set; }
        [Required]
        [MaxLength(50)]
        public string FPaymentMethod { get; set; } = null!;
        [MaxLength(255)]
        public string FPaypalOrderId { get; set; }
    }
}