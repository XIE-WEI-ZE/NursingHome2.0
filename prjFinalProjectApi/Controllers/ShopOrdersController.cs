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

        // 建立訂單
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] OrderDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (dto == null || dto.OrderDetails == null || !dto.OrderDetails.Any())
                return BadRequest("訂單資料不完整");

            // 產生訂單編號
            string orderNo = await GenerateOrderNo();

            var order = new ShopOrder
            {
                BuyerName = dto.BuyerName,
                ReceiverName = dto.ReceiverName,
                ReceiverPhone = dto.ReceiverPhone,
                PaymentMethod = dto.PaymentMethod,
                DeliveryMethod = dto.DeliveryMethod,
                DeliveryAddress = dto.DeliveryAddress,
                InvoiceTitle = dto.InvoiceTitle,
                InvoiceTax = dto.InvoiceTax,
                InvoiceInMethod = dto.InvoiceType,
                CarrierNumber = dto.CarrierNumber,
                OrderTime = DateTime.Now,
                TotalAmount = dto.TotalAmount,
                Note = dto.Note,
                Status = "未付款", // 預設狀態
                OrderNo = orderNo
            };

            _context.ShopOrders.Add(order);
            await _context.SaveChangesAsync();

            // 儲存訂單明細
            foreach (var d in dto.OrderDetails)
            {
                var detail = new ShopOrderDetail
                {
                    OrderId = order.OrderId,
                    ProductId = d.ProductId,
                    ProductName = d.ProductName,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Subtotal = d.Subtotal,
                    Discount = 0
                };
                _context.ShopOrderDetails.Add(detail);
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, orderNo = orderNo, orderId = order.OrderId });
        }

        // 訂單編號生成：ORD-YYYYMMDD-0001
        private async Task<string> GenerateOrderNo()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            string prefix = $"ORD-{today}-";

            int countToday = await _context.ShopOrders
                .Where(o => o.OrderTime.Date == DateTime.Today)
                .CountAsync();

            string orderNo = prefix + (countToday + 1).ToString("D4");
            return orderNo;
        }
    }
}
