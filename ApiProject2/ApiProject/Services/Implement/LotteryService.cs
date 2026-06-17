using ApiProject.DTO;
using ApiProject.Repositories.Interface;
using ApiProject.Services.Interface;
using Microsoft.Extensions.Logging;
using System.Text;
using ClosedXML.Excel;


namespace ApiProject.Services.Implement
{
    public class LotteryService : ILotteryService
    {
        private readonly ILotteryRepository _lottteryRepository;
        private readonly IGiftRepository _giftRepository;
        private readonly IEmailService _emailService;
        private readonly IKafkaProducerService _kafkaProducer;
        private readonly ILogger<LotteryService> _logger;

        public LotteryService(
            ILotteryRepository lotteryRepository,
            IGiftRepository giftRepository,
            IEmailService emailService,
            IKafkaProducerService kafkaProducer,
            ILogger<LotteryService> logger)
        {
            _lottteryRepository = lotteryRepository;
            _giftRepository = giftRepository;
            _emailService = emailService;
            _kafkaProducer = kafkaProducer;
            _logger = logger;
        }

        public async Task<RaffleDto.LotteryResult> RaffleGift(int giftId)
        {
            try
            {
                _logger.LogInformation("Starting lottery process for Gift ID: {GiftId}", giftId);

                var entries = await _lottteryRepository.GetRaffleEntries(giftId);

                if (entries == null || !entries.Any())
                {
                    throw new Exception("אין כרטיסים להגרלה עבור מתנה זו");
                }

                // חישוב נתונים לדוח
                int totalTicketsSold = entries.Sum(e => e.Quantity);
                int totalParticipants = entries.Select(e => e.UserId).Distinct().Count();

                var tickets = new List<int>();
                foreach (var entry in entries)
                {
                    for (int i = 0; i < entry.Quantity; i++)
                        tickets.Add(entry.UserId);
                }

                var random = new Random();
                var winnerUserId = tickets[random.Next(tickets.Count)];

                await _lottteryRepository.SetWinner(giftId, winnerUserId);

                var winner = await _lottteryRepository.GetWinnerDetails(winnerUserId);
                var gift = await _giftRepository.GetGiftById(giftId);

                // שליחת מייל לזוכה
                if (winner != null && gift != null)
                {
                    try
                    {
                        await _emailService.SendWinnerEmail(
                            winner.Email,
                            winner.FirstName + " " + winner.LastName,
                            gift.Name
                        );
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx, "Failed to send email but winner was saved");
                    }
                }

                var toSend = new RaffleDto.LotteryResult
                {
                    GiftId = giftId,
                    GiftName = gift?.Name ?? "Unknown",
                    WinnerUserId = winnerUserId,
                    WinnerName = winner != null ? winner.FirstName + " " + winner.LastName : "Unknown",
                    WinnerEmail = winner?.Email,
                    TotalTicketsSold = totalTicketsSold,
                    TotalParticipants = totalParticipants,
                    TotalIncome = totalTicketsSold * (gift?.TicketPrice ?? 0),
                    DonorName = gift.DonorModel.FirstName + " " + gift.DonorModel.LastName,

                };
                await AppendLotteryResult(toSend);
                await _kafkaProducer.PublishAsync("LotteryDrawn", toSend);
                return toSend;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during raffle for Gift ID: {GiftId}", giftId);
                throw;
            }
        }

        public async Task AppendLotteryResult(RaffleDto.LotteryResult result)
        {
            var path = "Reports/LotterySummary.csv";
            Directory.CreateDirectory("Reports");

            bool exists = File.Exists(path);

            using var writer = new StreamWriter(path, append: true, Encoding.UTF8);

            if (!exists)
            {
                await writer.WriteLineAsync(
                    "GiftId,  GiftName,   WinnerName,   WinnerEmail,   TotalTicketsSold,   TotalParticipants,   TotalIncome,   DonorName    "
                );
            }

            await writer.WriteLineAsync(
                $"{result.GiftId},{result.GiftName},{result.WinnerName},{result.WinnerEmail},{result.TotalTicketsSold},{result.TotalParticipants},{result.TotalIncome},{result.DonorName}"
            );
        }


    }
}