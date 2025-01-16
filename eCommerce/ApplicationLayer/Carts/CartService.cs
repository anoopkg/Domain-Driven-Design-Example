using System;
using System.Diagnostics; // for the Stopwatch example
using eCommerce.Helpers.Repository;
using eCommerce.DomainModelLayer.Customers;
using eCommerce.DomainModelLayer.Products;
using eCommerce.DomainModelLayer.Purchases;
using eCommerce.DomainModelLayer.Carts;
using eCommerce.DomainModelLayer.Services;
// using AutoMapper; // Removed since we're mapping manually now

namespace eCommerce.ApplicationLayer.Carts
{
    // ----------------------------------------------------------------------
    // 1) Domain-Specific Exceptions (Instead of raw "throw new Exception")
    // ----------------------------------------------------------------------
    public class CustomerNotFoundException : Exception
    {
        public Guid CustomerId { get; }
        public CustomerNotFoundException(Guid customerId)
            : base($"Customer was not found with this Id: {customerId}")
        {
            CustomerId = customerId;
        }
    }

    public class ProductNotFoundException : Exception
    {
        public Guid ProductId { get; }
        public ProductNotFoundException(Guid productId)
            : base($"Product was not found with this Id: {productId}")
        {
            ProductId = productId;
        }
    }

    // A minimal TimeoutException (optional; .NET also has System.TimeoutException)
    public class OperationTimeoutException : Exception
    {
        public OperationTimeoutException(string message) : base(message) { }
    }

    public class CartService : ICartService
    {
        IRepository<Customer> customerRepository;
        IRepository<Product> productRepository;
        IRepository<Cart> cartRepository;
        IUnitOfWork unitOfWork;
        TaxService taxDomainService;
        CheckoutService checkoutDomainService;

        public CartService(
            IRepository<Customer> customerRepository,
            IRepository<Product> productRepository,
            IRepository<Cart> cartRepository,
            IUnitOfWork unitOfWork,
            TaxService taxDomainService,
            CheckoutService checkoutDomainService)
        {
            this.customerRepository = customerRepository;
            this.productRepository = productRepository;
            this.cartRepository = cartRepository;
            this.unitOfWork = unitOfWork;
            this.taxDomainService = taxDomainService;
            this.checkoutDomainService = checkoutDomainService;
        }

        // ----------------------------------------------------------------------
        // Example of basic timeout handling (Stopwatch-based for demonstration).
        // In real scenarios, you might pass a CancellationToken to each method.
        // ----------------------------------------------------------------------
        private void CheckForTimeout(Stopwatch stopwatch, int maxMilliseconds = 5000)
        {
            if (stopwatch.ElapsedMilliseconds > maxMilliseconds)
            {
                throw new OperationTimeoutException("Operation took too long and timed out.");
            }
        }

        public CartDto Add(Guid customerId, CartProductDto productDto)
        {
            // Start a timer to demonstrate explicit timeout check
            var stopwatch = Stopwatch.StartNew();

            // 1) Get customer
            Customer customer = this.customerRepository.FindById(customerId);
            if (customer == null)
                throw new CustomerNotFoundException(customerId);

            CheckForTimeout(stopwatch);

            // 2) Find or create cart
            Cart cart = this.cartRepository.FindOne(new CustomerCartSpec(customerId));
            if (cart == null)
            {
                cart = Cart.Create(customer);
                this.cartRepository.Add(cart);
            }

            CheckForTimeout(stopwatch);

            // 3) Get product
            Product product = this.productRepository.FindById(productDto.ProductId);
            this.validateProduct(productDto.ProductId, product);

            // 4) Double Dispatch Pattern
            cart.Add(
                CartProduct.Create(
                    customer,
                    cart,
                    product,
                    productDto.Quantity,
                    taxDomainService
                )
            );

            CheckForTimeout(stopwatch);

            // 5) Commit changes
            this.unitOfWork.Commit();

            // 6) Manually map Cart -> CartDto (replacing AutoMapper)
            CartDto cartDto = this.MapCartToDto(cart);

            return cartDto;
        }

        public CartDto Remove(Guid customerId, Guid productId)
        {
            var stopwatch = Stopwatch.StartNew();

            // 1) Find cart
            Cart cart = this.cartRepository.FindOne(new CustomerCartSpec(customerId));
            this.validateCart(customerId, cart);

            CheckForTimeout(stopwatch);

            // 2) Validate product
            Product product = this.productRepository.FindById(productId);
            this.validateProduct(productId, product);

            CheckForTimeout(stopwatch);

            // 3) Remove product & commit
            cart.Remove(product);
            this.unitOfWork.Commit();

            CheckForTimeout(stopwatch);

            // 4) Manual mapping
            CartDto cartDto = this.MapCartToDto(cart);

            return cartDto;
        }

        public CartDto Get(Guid customerId)
        {
            var stopwatch = Stopwatch.StartNew();

            // 1) Find cart
            Cart cart = this.cartRepository.FindOne(new CustomerCartSpec(customerId));
            this.validateCart(customerId, cart);

            CheckForTimeout(stopwatch);

            // 2) Manual mapping
            CartDto cartDto = this.MapCartToDto(cart);

            return cartDto;
        }

        public CheckOutResultDto CheckOut(Guid customerId)
        {
            var stopwatch = Stopwatch.StartNew();
            CheckOutResultDto checkOutResultDto = new CheckOutResultDto();

            // 1) Get the cart
            Cart cart = this.cartRepository.FindOne(new CustomerCartSpec(customerId));
            this.validateCart(customerId, cart);

            CheckForTimeout(stopwatch);

            // 2) Validate customer
            Customer customer = this.customerRepository.FindById(cart.CustomerId);
            if (customer == null)
                throw new CustomerNotFoundException(cart.CustomerId);

            CheckForTimeout(stopwatch);

            // 3) Check domain logic
            Nullable<CheckOutIssue> checkOutIssue = this.checkoutDomainService.CanCheckOut(customer, cart);
            if (!checkOutIssue.HasValue)
            {
                // 4) Do actual checkout
                Purchase purchase = this.checkoutDomainService.Checkout(customer, cart);
                this.unitOfWork.Commit();

                CheckForTimeout(stopwatch);

                // 5) Manual mapping from Purchase -> CheckOutResultDto
                checkOutResultDto = this.MapPurchaseToCheckOutResult(purchase);
            }
            else
            {
                // Provide the checkout issue
                checkOutResultDto.CheckOutIssue = checkOutIssue;
            }

            return checkOutResultDto;
        }

        private void validateCart(Guid customerId, Cart cart)
        {
            if (cart == null)
                throw new CustomerNotFoundException(customerId);
        }

        private void validateProduct(Guid productId, Product product)
        {
            if (product == null)
                throw new ProductNotFoundException(productId);
        }

        // ----------------------------------------------------------------------
        // 2) Manual Mapping Methods (Replace AutoMapper)
        // ----------------------------------------------------------------------
        private CartDto MapCartToDto(Cart cart)
        {
            var cartDto = new CartDto
            {
                CartId = cart.Id,
                CustomerId = cart.CustomerId,
                CartItems = new System.Collections.Generic.List<CartProductDto>()
            };

            foreach (var cp in cart.Products)
            {
                cartDto.CartItems.Add(
                    new CartProductDto
                    {
                        ProductId = cp.ProductId,
                        Quantity = cp.Quantity
                        // Include any other properties needed
                    }
                );
            }

            return cartDto;
        }

        private CheckOutResultDto MapPurchaseToCheckOutResult(Purchase purchase)
        {
            // Minimal manual mapping example
            return new CheckOutResultDto
            {
                PurchaseId = purchase.Id,
                // If needed, copy relevant fields from Purchase
            };
        }
    }
}
