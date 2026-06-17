using ApiProject.DTO;
using ApiProject.Models;
using ApiProject.Repositories.Interface;
using ApiProject.Services.Interface;

namespace ApiProject.Services
{
    public class CartService : ICartService
    {
        private readonly ICartRepository _repository;
        private readonly ILogger<CartService> _logger;
        private readonly IGiftsService _giftsService;
        private readonly IKafkaProducerService _kafkaProducer;

        public CartService(ICartRepository repository, ILogger<CartService> logger, IGiftsService giftsService, IKafkaProducerService kafkaProducer)
        {
            _repository = repository;
            _logger = logger;
            _giftsService = giftsService;
            _kafkaProducer = kafkaProducer;
        }

        public async Task<CartDto.CartModelDto> CreateCart(int UserId)
        {
            try
            {
                var cart = new CartModel
                {
                    UserModelId = UserId,
                    MyStatus = Status.Draft,
                    CartItem = new List<CartItemModel>()
                };

                var create = await _repository.CreateCart(cart);

                _logger.LogInformation(
                    "Created new cart {CartId} for user {UserId}",
                    create.Id,
                    create.UserModelId
                );

                return new CartDto.CartModelDto
                {
                    Id = create.Id,
                    UserId = create.UserModelId,
                    Status = create.MyStatus,
                    Items = new List<CartDto.CartItemDto>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating cart for user {UserId}", UserId);
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> GetCartByUserId(int userId)
        {
            try
            {
                var cart = await _repository.GetCartByUserId(userId);
                if (cart == null) {
                     await CreateCart(userId);
                    cart=await _repository.GetCartByUserId(userId);
                }

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching cart for user {UserId}", userId);
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> GetUnpaidCartByUserId(int userId)
        {
            try
            {
                var cart = await _repository.GetUnpaidCartByUserId(userId);
                if (cart == null)
                    return await CreateCart(userId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Image = item.Gift.Image

                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching unpaid cart for user {UserId}", userId);
                throw;
            }
        }

        public async Task<List<CartDto.CartModelDto>> GetPaidCartByUserId(int userId)
        {
            try
            {
                return await _repository.GetAllPaidCartDtosByUserId(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paid carts for user {UserId}", userId);
                throw;
            }
        }

        public async Task<CartDto.CartItemDto> GetCartItemByGiftId(int userId, int giftId)
        {
            try
            {
                var cart = await _repository.GetOrCreateDraftCartByUserId(userId);
                if (cart == null) return null;

                var cartItem = await _repository.CheckIfUserHaveThisGift(cart.Id, giftId);
                if (cartItem == null) return null;

                return new CartDto.CartItemDto
                {
                    GiftId = cartItem.GiftModelId,
                    GiftName = cartItem.Gift.Name,
                    Quantity = cartItem.Quantity,
                    TicketPrice = cartItem.Gift.TicketPrice,
                    Image = cartItem.Gift.Image

                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error fetching cart item {GiftId} for user {UserId}",
                    giftId,
                    userId
                );
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> UpdateGiftToCart(CartDto.UpdateCartItemDto updateCartItem)
        {
            try
            {
                var cart = await _repository.GetOrCreateDraftCartByUserId(updateCartItem.UserId);
                if (cart == null)
                    await CreateCart(updateCartItem.UserId);

                var cartItem = await _repository.CheckIfUserHaveThisGift(cart.Id, updateCartItem.GiftId);
                if (cartItem == null)
                    return null;

                cartItem.Quantity = updateCartItem.Quantity;
                await _repository.UpdateGiftToCart(cartItem);

                _logger.LogInformation(
                    "Updated quantity of gift {GiftId} to {Quantity} for cart {CartId}",
                    cartItem.GiftModelId,
                    cartItem.Quantity,
                    cart.Id
                );

                cart = await _repository.GetOrCreateDraftCartByUserId(updateCartItem.UserId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Image = item.Gift.Image

                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error updating gift {GiftId} for user {UserId}",
                    updateCartItem.GiftId,
                    updateCartItem.UserId
                );
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> AddGiftToCart(CartDto.AddCartItemDto newItem)
        {
            try
            {
                var check = await _repository.CheckGiftIsRaffled(newItem.GiftId);
                if (check == true)
                    return null;

                var cart = await _repository.GetOrCreateDraftCartByUserId(newItem.UserId);
                var existAlready = await _repository.CheckIfUserHaveThisGift(cart.Id, newItem.GiftId);

                if (existAlready != null)
                {
                    _logger.LogInformation(
                        "Gift {GiftId} already exists in cart {CartId}, increasing quantity",
                        newItem.GiftId,
                        cart.Id
                    );
                    return await PlusOneGiftToCart(newItem);
                }

                var cartItem = new CartItemModel
                {
                    CartId = cart.Id,
                    GiftModelId = newItem.GiftId,
                    Quantity = 1
                };

                await _repository.AddGiftToCart(cartItem);

                _logger.LogInformation(
                    "Added gift {GiftId} to cart {CartId}",
                    newItem.GiftId,
                    cart.Id
                );

                cart = await _repository.GetOrCreateDraftCartByUserId(newItem.UserId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Image = item.Gift.Image

                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error adding gift {GiftId} for user {UserId}",
                    newItem.GiftId,
                    newItem.UserId
                );
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> PlusOneGiftToCart(CartDto.AddCartItemDto addCartItem)
        {
            try
            {
                var cart = await _repository.GetOrCreateDraftCartByUserId(addCartItem.UserId);
                var existAlready = await _repository.CheckIfUserHaveThisGift(cart.Id, addCartItem.GiftId);
                if (existAlready == null) return null;

                existAlready.Quantity += 1;
                await _repository.UpdateGiftToCart(existAlready);

                _logger.LogInformation(
                    "Increased quantity of gift {GiftId} to {Quantity} in cart {CartId}",
                    existAlready.GiftModelId,
                    existAlready.Quantity,
                    cart.Id
                );

                cart = await _repository.GetOrCreateDraftCartByUserId(addCartItem.UserId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Image = item.Gift.Image


                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error increasing gift {GiftId} for user {UserId}",
                    addCartItem.GiftId,
                    addCartItem.UserId
                );
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> MinusOneGiftToCart(CartDto.AddCartItemDto addCartItem)
        {
            try
            {
                var cart = await _repository.GetOrCreateDraftCartByUserId(addCartItem.UserId);
                var existAlready = await _repository.CheckIfUserHaveThisGift(cart.Id, addCartItem.GiftId);
                if (existAlready == null) return null;

                if (existAlready.Quantity == 1)
                {
                    _logger.LogInformation(
                        "Quantity is 1, deleting gift {GiftId} from cart {CartId}",
                        existAlready.GiftModelId,
                        cart.Id
                    );
                    return await DeleteGiftFromCart(addCartItem.UserId, addCartItem.GiftId);
                }

                existAlready.Quantity -= 1;
                await _repository.UpdateGiftToCart(existAlready);

                _logger.LogInformation(
                    "Decreased quantity of gift {GiftId} to {Quantity} in cart {CartId}",
                    existAlready.GiftModelId,
                    existAlready.Quantity,
                    cart.Id
                );

                cart = await _repository.GetOrCreateDraftCartByUserId(addCartItem.UserId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Image = item.Gift.Image


                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error decreasing gift {GiftId} for user {UserId}",
                    addCartItem.GiftId,
                    addCartItem.UserId
                );
                throw;
            }
        }

        public async Task<CartDto.CartModelDto> DeleteGiftFromCart(int userId, int giftId)
        {
            try
            {
                await _repository.DeleteGiftFromCart(userId, giftId);

                _logger.LogInformation(
                    "Deleted gift {GiftId} from user {UserId} cart",
                    giftId,
                    userId
                );

                var cart = await _repository.GetOrCreateDraftCartByUserId(userId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Image = item.Gift.Image


                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error deleting gift {GiftId} for user {UserId}",
                    giftId,
                    userId
                );
                throw;
            }
        }

        public async Task<List<CartDto.CartModelDto>> PurchaseCart(int userId)
        {
            try
            {
                var unpaidCart = await _repository.GetUnpaidCartByUserId(userId);
                if (unpaidCart == null)
                {
                    _logger.LogInformation("No unpaid cart found for user {UserId}", userId);
                    return await _repository.GetAllPaidCartDtosByUserId(userId);
                }
                foreach (var item in unpaidCart.CartItem)
                {
                    var isRaffled = await _repository.CheckGiftIsRaffled(item.GiftModelId);
                    if (isRaffled)
                    {
                        _logger.LogWarning("Cannot purchase cart {CartId} for user {UserId} because gift {GiftId} is already raffled", unpaidCart.Id, userId, item.GiftModelId);

                        throw new InvalidOperationException(
                            $"Cannot purchase cart because gift '{item.Gift.Name}' has already been raffled."
                        );
                    }
                }

                await _repository.PurchaseCart(unpaidCart);

                _logger.LogInformation(
                    "Purchased cart {CartId} for user {UserId}", unpaidCart.Id, userId);

                await _kafkaProducer.PublishAsync("OrderCreated", new
                {
                    CartId = unpaidCart.Id,
                    UserId = unpaidCart.UserModelId,
                    Items = unpaidCart.CartItem.Select(item => new
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice
                    }),
                    TotalAmount = unpaidCart.CartItem.Sum(item => item.Quantity * item.Gift.TicketPrice)
                });

                return await _repository.GetAllPaidCartDtosByUserId(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error purchasing cart for user {UserId}", userId);
                throw;
            }
        }
        public async Task<CartDto.CartModelDto> MergeGuestCart(int userId, List<CartDto.MergeDto> guestItems)
        {
            try
            {
                var cart = await _repository.GetOrCreateDraftCartByUserId(userId);

                foreach (var item in guestItems)
                {
                    var existAlready = await _repository.CheckIfUserHaveThisGift(cart.Id, item.GiftId);
                    if (existAlready != null)
                    {
                        existAlready.Quantity += item.Quantity;
                        await _repository.UpdateGiftToCart(existAlready);

                        _logger.LogInformation(
                            "Merged {Quantity} of gift {GiftId} into existing cart {CartId}",
                            item.Quantity,
                            item.GiftId,
                            cart.Id
                        );
                    }
                    else
                    {
                        await _repository.AddGiftToCart(new CartItemModel
                        {
                            CartId = cart.Id,
                            GiftModelId = item.GiftId,
                            Quantity = item.Quantity
                        });

                        _logger.LogInformation(
                            "Added new gift {GiftId} x {Quantity} to cart {CartId}",
                            item.GiftId,
                            item.Quantity,
                            cart.Id
                        );
                    }
                }

                cart = await _repository.GetOrCreateDraftCartByUserId(userId);

                return new CartDto.CartModelDto
                {
                    Id = cart.Id,
                    UserId = cart.UserModelId,
                    Status = cart.MyStatus,
                    Items = cart.CartItem.Select(item => new CartDto.CartItemDto
                    {
                        GiftId = item.GiftModelId,
                        GiftName = item.Gift.Name,
                        Quantity = item.Quantity,
                        TicketPrice = item.Gift.TicketPrice,
                        Category = item.Gift.CategoryModel.Name,
                        Image = item.Gift.Image


                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error merging guest cart for user {UserId}", userId);
                throw;
            }
        }
    }
}
