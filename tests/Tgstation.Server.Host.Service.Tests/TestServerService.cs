﻿using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.Watchdog;

namespace Tgstation.Server.Host.Service.Tests
{
	/// <summary>
	/// Tests for <see cref="ServerService"/>
	/// </summary>
	[TestClass]
	public sealed class TestServerService
	{
		[TestMethod]
		public void TestConstructionAndDisposal()
		{
			Assert.ThrowsException<ArgumentNullException>(() => new ServerService(null, null, null, default));
			var mockWatchdogFactory = new Mock<IWatchdogFactory>();
			Assert.ThrowsException<ArgumentNullException>(() => new ServerService(mockWatchdogFactory.Object, null, null, default));
			LoggerFactory.Create(builder => Assert.ThrowsException<ArgumentNullException>(() => new ServerService(mockWatchdogFactory.Object, builder, null, default)));
			LoggerFactory.Create(builder => { new ServerService(mockWatchdogFactory.Object, builder, () => null, default).Dispose(); });
		}

		[TestMethod]
		public void TestRun()
		{
			var type = typeof(ServerService);
			var onStart = type.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic);
			var onStop = type.GetMethod("OnStop", BindingFlags.Instance | BindingFlags.NonPublic);

			var mockWatchdog = new Mock<IWatchdog>();
			var args = Array.Empty<string>();
			CancellationToken cancellationToken, originalToken;
			using(var cts = new CancellationTokenSource())
			{
				originalToken = cts.Token;
				cancellationToken = originalToken;
			}
			var cancelled = false;
			
			mockWatchdog.Setup(x => x.RunAsync(false, args, It.IsAny<CancellationToken>())).Callback((bool x, string[] _, CancellationToken token) => token.Register(() => cancelled = true)).Returns(Task.CompletedTask).Verifiable();
			var mockWatchdogFactory = new Mock<IWatchdogFactory>();

			ServerService service = null;
			ILoggerFactory loggerFactory = null;
			using (loggerFactory = LoggerFactory.Create(builder => service = new ServerService(mockWatchdogFactory.Object, builder, () => loggerFactory, default)))
			using (service)
			{
				mockWatchdogFactory.Setup(x => x.CreateWatchdog(loggerFactory)).Returns(mockWatchdog.Object).Verifiable();
				Assert.IsFalse(cancelled);
				onStart.Invoke(service, new object[] { args });
				mockWatchdog.VerifyAll();

				Assert.AreNotSame(cancellationToken, originalToken);

				onStop.Invoke(service, Array.Empty<object>());
				Assert.IsTrue(cancelled);
			}
			mockWatchdogFactory.VerifyAll();
		}
	}
}
