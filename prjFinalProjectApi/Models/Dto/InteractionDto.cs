using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models.Dto
{
    public class InteractionDto
    {
        [Required]
        public int MemberId { get; set; }

        [Required]
        [StringLength(50)]
        public string InteractionType { get; set; }
    }
}
