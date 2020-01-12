﻿using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tgstation.Server.Host.IO
{
	/// <inheritdoc />
	sealed class Console : IConsole, IDisposable
	{
		/// <inheritdoc />
		public bool Available => Environment.UserInteractive;

		/// <inheritdoc />
		public CancellationToken CancelKeyPress => cancelKeyCts.Token;

		/// <summary>
		/// The <see cref="CancellationTokenSource"/> for <see cref="CancelKeyPress"/>.
		/// </summary>
		readonly CancellationTokenSource cancelKeyCts;

		/// <summary>
		/// If the <see cref="Console"/> was disposed;
		/// </summary>
		bool disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="Console"/> <see langword="class"/>.
		/// </summary>
		public Console()
		{
			cancelKeyCts = new CancellationTokenSource();
			System.Console.CancelKeyPress += (sender, e) =>
			{
				lock (cancelKeyCts)
				{
					if (!disposed)
						cancelKeyCts.Cancel();
				}
			};
		}

		/// <inheritdoc />
		public void Dispose()
		{
			lock (cancelKeyCts)
			{
				cancelKeyCts.Dispose();
				disposed = true;
			}
		}

		void CheckAvailable()
		{
			if (!Available)
				throw new InvalidOperationException("Console unavailable");
		}

		/// <inheritdoc />
		public Task PressAnyKeyAsync(CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			CheckAvailable();
			System.Console.Read();
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public Task<string> ReadLineAsync(bool usePasswordChar, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			// TODO: Make this better: https://stackoverflow.com/questions/9479573/how-to-interrupt-console-readline
			CheckAvailable();
			if (!usePasswordChar)
				return System.Console.ReadLine();

			var passwordBuilder = new StringBuilder();
			do
			{
				var keyDescription = System.Console.ReadKey(true);
				if (keyDescription.Key == ConsoleKey.Enter)
					break;
				else if (keyDescription.Key == ConsoleKey.Backspace)
				{
					if (passwordBuilder.Length > 0)
					{
						--passwordBuilder.Length;
						System.Console.Write("\b \b");
					}
				}
				else if (keyDescription.KeyChar != '\u0000')
				{
					// KeyChar == '\u0000' if the key pressed does not correspond to a printable character, e.g. F1, Pause-Break, etc
					passwordBuilder.Append(keyDescription.KeyChar);
					System.Console.Write('*');
				}
			}
			while (!cancellationToken.IsCancellationRequested);

			cancellationToken.ThrowIfCancellationRequested();
			System.Console.WriteLine();
			return passwordBuilder.ToString();
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public Task WriteAsync(string text, bool newLine, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			CheckAvailable();
			if (text == null)
			{
				if (!newLine)
					throw new InvalidOperationException("Cannot write null text without a new line!");
				System.Console.WriteLine();
			}
			else if (newLine)
				System.Console.WriteLine(text);
			else
				System.Console.Write(text);
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);
	}
}
