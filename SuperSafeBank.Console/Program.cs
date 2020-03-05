﻿using System;
using System.Threading;
using System.Threading.Tasks;
using SuperSafeBank.Core;
using SuperSafeBank.Domain;
using SuperSafeBank.Domain.Events;
using SuperSafeBank.Domain.Services;
using SuperSafeBank.Persistence.EventStore;
using SuperSafeBank.Persistence.Kafka;

namespace SuperSafeBank.Console
{ 
    public class Program
    {
        static async Task Main(string[] args)
        {
            var kafkaConnString = "localhost:9092";
            var eventsTopic = "events";

            var cts = new CancellationTokenSource();

            System.Console.CancelKeyPress += (s, e) =>
            {
                System.Console.WriteLine("shutting down...");
                e.Cancel = true;
                cts.Cancel();
            };

            var jsonEventDeserializer = new JsonEventDeserializer(new []
            {
                typeof(AccountCreated).Assembly
            });

            var consumer = new EventConsumer<Account, Guid>(eventsTopic, kafkaConnString, jsonEventDeserializer);
            consumer.EventReceived += Consumer_EventReceived;
            var tc = consumer.ConsumeAsync(cts.Token);

            var tp = Write(eventsTopic, kafkaConnString, jsonEventDeserializer);

            await Task.WhenAll(tp, tc);

            System.Console.WriteLine("done!");
            System.Console.ReadLine();
        }

        private static void Consumer_EventReceived(object sender, Core.Models.IDomainEvent<Guid> e)
        {
            System.Console.WriteLine($"processing event {e.GetType()} ...");
        }

        private static async Task Write(string eventsTopic, 
            string kafkaConnString,
            JsonEventDeserializer jsonEventDeserializer)
        {
            var eventStoreConnStr = new Uri("tcp://admin:changeit@localhost:1113");
            var connectionWrapper = new EventStoreConnectionWrapper(eventStoreConnStr);

            var customerEventsRepository = new EventsRepository<Customer, Guid>(connectionWrapper, jsonEventDeserializer);
            var customerEventsProducer = new EventProducer<Customer, Guid>(eventsTopic, kafkaConnString);

            var accountEventsRepository = new EventsRepository<Account, Guid>(connectionWrapper, jsonEventDeserializer);
            var accountEventsProducer = new EventProducer<Account, Guid>(eventsTopic, kafkaConnString);

            var customerEventsService = new EventsService<Customer, Guid>(customerEventsRepository, customerEventsProducer);
            var accountEventsService = new EventsService<Account, Guid>(accountEventsRepository, accountEventsProducer);

            var currencyConverter = new FakeCurrencyConverter();

            var customer = Customer.Create("lorem", "ipsum");
            await customerEventsService.PersistAsync(customer);

            var account = Account.Create(customer, Currency.CanadianDollar);
            account.Deposit(new Money(Currency.CanadianDollar, 10), currencyConverter);
            account.Deposit(new Money(Currency.CanadianDollar, 42), currencyConverter);
            account.Withdraw(new Money(Currency.CanadianDollar, 4), currencyConverter);
            account.Deposit(new Money(Currency.CanadianDollar, 71), currencyConverter);
            await accountEventsService.PersistAsync(account);

            account.Withdraw(new Money(Currency.CanadianDollar, 10), currencyConverter);
            account.Deposit(new Money(Currency.CanadianDollar, 11), currencyConverter);
            await accountEventsService.PersistAsync(account);
        }
    }
}
