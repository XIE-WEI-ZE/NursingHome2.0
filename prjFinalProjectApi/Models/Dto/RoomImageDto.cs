using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace prjFinalProjectApi.Models.Dto
{
    public class RoomImageDto
    {
        [Required]
        public int FRoomId { get; set; }
        [Required]
        public IFormFile[] RoomImages { get; set; } = null!;
    }
}
