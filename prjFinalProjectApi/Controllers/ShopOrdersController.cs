using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ShopOrdersController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;

        public ShopOrdersController(DbNursingHomeContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 會員的訂單清單（分頁 + 訂單編號關鍵字）
        /// GET /api/ShopOrders/member/{memberId}?page=1&pageSize=10&keyword=ORD-2025
        /// </summary>
        [HttpGet("member/{memberId:int}")]
        [ProducesResponseType(typeof(PagedResult<OrderListItemDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMemberOrders(
            int memberId,
            int page = 1,
            int pageSize = 10,
            string? keyword = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 100) pageSize = 10;

            var q = _context.ShopOrders
                            .AsNoTracking()
                            .Where(o => o.FMemberId == memberId);

            if (!string.IsNullOrWhiteSpace(keyword))
                q = q.Where(o => o.OrderNo != null && o.OrderNo.Contains(keyword));

            var total = await q.CountAsync();

            var items = await q.OrderByDescending(o => o.OrderTime)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .Select(o => new OrderListItemDto
                               {
                                   OrderId = o.OrderId,
                                   OrderNo = o.OrderNo ?? string.Empty,
                                   OrderTime = o.OrderTime,
                                   TotalAmount = o.TotalAmount ?? 0,   // 防止 int? -> int
                                   Status = o.Status ?? string.Empty
                               })
                               .ToListAsync();

            var result = new PagedResult<OrderListItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Items = items
            };
            return Ok(result);
        }

        /// <summary>
        /// 會員單筆訂單（含明細）
        /// GET /api/ShopOrders/member/{memberId}/{orderId}
        /// </summary>
        [HttpGet("member/{memberId:int}/{orderId:long}")]
        [ProducesResponseType(typeof(OrderViewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMemberOrderById(int memberId, long orderId)
        {
            // 先查主檔
            var order = await _context.ShopOrders
                .AsNoTracking()
                .Where(o => o.OrderId == orderId && o.FMemberId == memberId)
                .Select(o => new OrderViewDto
                {
                    OrderId = o.OrderId,
                    OrderNo = o.OrderNo ?? string.Empty,
                    OrderTime = o.OrderTime,
                    BuyerName = o.BuyerName,
                    ReceiverName = o.ReceiverName,
                    ReceiverPhone = o.ReceiverPhone,
                    PaymentMethod = o.PaymentMethod,
                    DeliveryMethod = o.DeliveryMethod,
                    DeliveryAddress = o.DeliveryAddress,
                    InvoiceTitle = o.InvoiceTitle,
                    InvoiceTax = o.InvoiceTax,
                    InvoiceInMethod = o.InvoiceInMethod,
                    CarrierNumber = o.CarrierNumber,
                    Note = o.Note,
                    Status = o.Status,
                    TotalAmount = o.TotalAmount ?? 0,
                    Details = new List<OrderDetailViewDto>() // 後面補
                })
                .FirstOrDefaultAsync();

            if (order == null) return NotFound();

            // 再查明細
            order.Details = await _context.ShopOrderDetails
                .AsNoTracking()
                .Where(d => d.OrderId == orderId)
                .Select(d => new OrderDetailViewDto
                {
                    DetailId = d.DetailId,
                    ProductId = d.ProductId,
                    ProductName = d.ProductName ?? string.Empty,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Subtotal = d.Subtotal
                })
                .ToListAsync();

            return Ok(order);
        }

        /// <summary>
        /// 會員單筆訂單（依訂單編號，含明細）
        /// GET /api/ShopOrders/member/{memberId}/by-no/{orderNo}
        /// </summary>
        [HttpGet("member/{memberId:int}/by-no/{orderNo}")]
        [ProducesResponseType(typeof(OrderViewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMemberOrderByNo(int memberId, string orderNo)
        {
            // 先查主檔
            var order = await _context.ShopOrders
                .AsNoTracking()
                .Where(o => o.FMemberId == memberId && o.OrderNo == orderNo)
                .Select(o => new OrderViewDto
                {
                    OrderId = o.OrderId,
                    OrderNo = o.OrderNo ?? string.Empty,
                    OrderTime = o.OrderTime,
                    BuyerName = o.BuyerName,
                    ReceiverName = o.ReceiverName,
                    ReceiverPhone = o.ReceiverPhone,
                    PaymentMethod = o.PaymentMethod,
                    DeliveryMethod = o.DeliveryMethod,
                    DeliveryAddress = o.DeliveryAddress,
                    InvoiceTitle = o.InvoiceTitle,
                    InvoiceTax = o.InvoiceTax,
                    InvoiceInMethod = o.InvoiceInMethod,
                    CarrierNumber = o.CarrierNumber,
                    Note = o.Note,
                    Status = o.Status,
                    TotalAmount = o.TotalAmount ?? 0,
                    Details = new List<OrderDetailViewDto>()
                })
                .FirstOrDefaultAsync();

            if (order == null) return NotFound();

            // 再查明細
            order.Details = await _context.ShopOrderDetails
                .AsNoTracking()
                .Where(d => d.OrderId == order.OrderId)
                .Select(d => new OrderDetailViewDto
                {
                    DetailId = d.DetailId,
                    ProductId = d.ProductId,
                    ProductName = d.ProductName ?? string.Empty,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Subtotal = d.Subtotal
                })
                .ToListAsync();

            return Ok(order);
        }
    }
}
