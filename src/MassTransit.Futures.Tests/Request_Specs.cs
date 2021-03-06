﻿// Copyright 2007-2018 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the License for the
// specific language governing permissions and limitations under the License.
namespace MassTransit.Tests
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using GreenPipes;
    using NUnit.Framework;
    using TestFramework;
    using Util;


    [TestFixture]
    public class SmartEndpointTestFixture :
        InMemoryTestFixture
    {
        protected override void ConfigureInMemoryBus(IInMemoryBusFactoryConfigurator configurator)
        {
        }
    }


    [TestFixture]
    public class Using_the_new_request_client_syntax :
        InMemoryTestFixture
    {
        [Test]
        public async Task Should_be_awesome()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress);

            Response<Value> response = await client.GetResponse<Value>(new GetValue());

            Assert.That(response.RequestId.HasValue, Is.True);
        }

        [Test]
        public async Task Should_be_awesome_with_a_side_of_sourdough_toast()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress);

            Response<Value> response;
            using (RequestHandle<GetValue> request = client.Create(new GetValue()))
            {
                request.UseExecute(context => context.Headers.Set("Frank", "Mary"));

                response = await request.GetResponse<Value>();
            }

            Assert.That(response.RequestId.HasValue, Is.True);
            Assert.That(response.Headers.TryGetHeader("Frank", out object value), Is.True);
            Assert.That(value, Is.EqualTo("Mary"));
        }

        [Test]
        public async Task Should_throw_the_request_exception()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress);

            Assert.That(async () => await client.GetResponse<Value>(new GetValue {BlowUp = true}), Throws.TypeOf<RequestFaultException>());
        }

        [Test]
        public async Task Should_throw_the_request_exception_including_fault()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress);

            try
            {
                await client.GetResponse<Value>(new GetValue {BlowUp = true});

                Assert.Fail("Should have thrown");
            }
            catch (RequestFaultException exception)
            {
                Assert.That(exception.Fault.Exceptions.First().ExceptionType, Is.EqualTo(TypeMetadataCache<IntentionalTestException>.ShortName));
                Assert.That(exception.RequestType, Is.EqualTo(TypeMetadataCache<GetValue>.ShortName));
            }
            catch
            {
                Assert.Fail("Unknown exception type thrown");
            }
        }

        [Test]
        public async Task Should_throw_a_timeout_exception()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress, MassTransit.RequestTimeout.After(s: 1));

            Assert.That(async () => await client.GetResponse<Value>(new GetValue {Discard = true}), Throws.TypeOf<RequestTimeoutException>());
        }

        [Test]
        public async Task Should_throw_a_timeout_exception_with_milliseconds()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress, 100);

            Assert.That(async () => await client.GetResponse<Value>(new GetValue {Discard = true}), Throws.TypeOf<RequestTimeoutException>());
        }

        [Test]
        public async Task Should_throw_canceled_exception_when_canceled()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress);

            using (var source = new CancellationTokenSource(100))
            {
                Assert.That(async () => await client.GetResponse<Value>(new GetValue {Discard = true}, source.Token), Throws.TypeOf<TaskCanceledException>());
            }
        }

        [Test]
        public async Task Should_throw_canceled_exception_when_already_canceled()
        {
            var client = Bus.CreateRequestClient<GetValue>(InputQueueAddress);

            using (var source = new CancellationTokenSource())
            {
                source.Cancel();

                Assert.That(async () => await client.GetResponse<Value>(new GetValue {Discard = true}, source.Token), Throws.TypeOf<TaskCanceledException>());
            }
        }

        protected override void ConfigureInMemoryReceiveEndpoint(IInMemoryReceiveEndpointConfigurator configurator)
        {
            Handler<GetValue>(configurator, async context =>
            {
                if (context.Message.Discard)
                    return;

                if (context.Message.BlowUp)
                    throw new IntentionalTestException("I hate it when this happens");

                await context.RespondAsync(new Value(), responseContext =>
                {
                    if (!responseContext.TryGetPayload(out ConsumeContext cc))
                        throw new InvalidOperationException("Expected to find a ConsumeContext there");
                });
            });
        }


        public class GetValue
        {
            public bool Discard { get; set; }
            public bool BlowUp { get; set; }
        }


        public class Value
        {
        }
    }


    [TestFixture]
    public class Using_the_request_with_multiple_result_types :
        InMemoryTestFixture
    {
        [Test]
        public async Task Should_match_the_first_result()
        {
            var client = Bus.CreateRequestClient<RegisterMember>(InputQueueAddress);

            var (registered, existing) = await client.GetResponse<MemberRegistered, ExistingMemberFound>(new RegisterMember());

            Assert.That(registered.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(existing.Status, Is.EqualTo(TaskStatus.Canceled));
        }

        [Test]
        public async Task Should_match_the_second_result()
        {
            var client = Bus.CreateRequestClient<RegisterMember>(InputQueueAddress);

            var (registered, existing) = await client.GetResponse<MemberRegistered, ExistingMemberFound>(new RegisterMember() {MemberId = "Johnny5"});

            Assert.That(registered.Status, Is.EqualTo(TaskStatus.Canceled));
            Assert.That(existing.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        protected override void ConfigureInMemoryReceiveEndpoint(IInMemoryReceiveEndpointConfigurator configurator)
        {
            Handler<RegisterMember>(configurator, context =>
            {
                if (context.Message.MemberId == "Johnny5")
                    return context.RespondAsync<ExistingMemberFound>(new
                    {
                        context.Message.MemberId,
                    });

                return context.RespondAsync<MemberRegistered>(new
                {
                    context.Message.MemberId,
                });
            });
        }


        public class RegisterMember
        {
            public string MemberId { get; set; }
        }


        public class MemberRegistered
        {
            public string MemberId { get; set; }
        }


        public class ExistingMemberFound
        {
            public string MemberId { get; set; }
        }
    }
}