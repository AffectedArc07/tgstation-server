﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Host.Core
{
	/// <inheritdoc />
	sealed class SetupWizard : ISetupWizard
	{
		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IConsole"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IConsole console;

		/// <summary>
		/// The <see cref="IHostingEnvironment"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IHostingEnvironment hostingEnvironment;

		/// <summary>
		/// The <see cref="IApplication"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IApplication application;

		/// <summary>
		/// The <see cref="IDBConnectionFactory"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IDBConnectionFactory dbConnectionFactory;

		/// <summary>
		/// The <see cref="IPlatformIdentifier"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IPlatformIdentifier platformIdentifier;

		/// <summary>
		/// The <see cref="IAsyncDelayer"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly IAsyncDelayer asyncDelayer;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly ILogger<SetupWizard> logger;

		/// <summary>
		/// The <see cref="GeneralConfiguration"/> for the <see cref="SetupWizard"/>
		/// </summary>
		readonly GeneralConfiguration generalConfiguration;

		/// <summary>
		/// Construct a <see cref="SetupWizard"/>
		/// </summary>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="console">The value of <see cref="console"/></param>
		/// <param name="hostingEnvironment">The value of <see cref="hostingEnvironment"/></param>
		/// <param name="application">The value of <see cref="application"/></param>
		/// <param name="dbConnectionFactory">The value of <see cref="dbConnectionFactory"/></param>
		/// <param name="platformIdentifier">The value of <see cref="platformIdentifier"/></param>
		/// <param name="asyncDelayer">The value of <see cref="asyncDelayer"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="generalConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="generalConfiguration"/></param>
		public SetupWizard(IIOManager ioManager, IConsole console, IHostingEnvironment hostingEnvironment, IApplication application, IDBConnectionFactory dbConnectionFactory, IPlatformIdentifier platformIdentifier, IAsyncDelayer asyncDelayer, ILogger<SetupWizard> logger, IOptions<GeneralConfiguration> generalConfigurationOptions)
		{
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.console = console ?? throw new ArgumentNullException(nameof(console));
			this.hostingEnvironment = hostingEnvironment ?? throw new ArgumentNullException(nameof(hostingEnvironment));
			this.application = application ?? throw new ArgumentNullException(nameof(application));
			this.dbConnectionFactory = dbConnectionFactory ?? throw new ArgumentNullException(nameof(dbConnectionFactory));
			this.platformIdentifier = platformIdentifier ?? throw new ArgumentNullException(nameof(platformIdentifier));
			this.asyncDelayer = asyncDelayer ?? throw new ArgumentNullException(nameof(asyncDelayer));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			generalConfiguration = generalConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(generalConfigurationOptions));
		}

		/// <summary>
		/// A prompt for a yes or no value
		/// </summary>
		/// <param name="question">The question <see cref="string"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> resulting in <see langword="true"/> if the user replied yes, <see langword="false"/> otherwise</returns>
		async Task<bool> PromptYesNo(string question, CancellationToken cancellationToken)
		{
			do
			{
				await console.WriteAsync(question, false, cancellationToken).ConfigureAwait(false);
				var responseString = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
				var upperResponse = responseString.ToUpperInvariant();
				if (upperResponse == "Y" || upperResponse == "YES")
					return true;
				else if (upperResponse == "N" || upperResponse == "NO")
					return false;
				await console.WriteAsync("Invalid response!", true, cancellationToken).ConfigureAwait(false);
			}
			while (true);
		}

		/// <summary>
		/// Prompts the user to enter the port to host TGS on
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> resulting in the hosting port, or <see langword="null"/> to use the default</returns>
		async Task<ushort?> PromptForHostingPort(CancellationToken cancellationToken)
		{
			await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
			await console.WriteAsync("What port would you like to connect to TGS on?", true, cancellationToken).ConfigureAwait(false);
			await console.WriteAsync("Note: If this is a docker container with the default port already mapped, use the default.", true, cancellationToken).ConfigureAwait(false);

			do
			{
				await console.WriteAsync("API Port (leave blank for default): ", false, cancellationToken).ConfigureAwait(false);
				var portString = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
				if (String.IsNullOrWhiteSpace(portString))
					return null;
				if (UInt16.TryParse(portString, out var port) && port != 0)
					return port;
				await console.WriteAsync("Invalid port! Please enter a value between 1 and 65535", true, cancellationToken).ConfigureAwait(false);
			}
			while (true);
		}

		/// <summary>
		/// Prompts the user to create a <see cref="DatabaseConfiguration"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the new <see cref="DatabaseConfiguration"/></returns>
		async Task<DatabaseConfiguration> ConfigureDatabase(CancellationToken cancellationToken)
		{
			do
			{
				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync("What SQL database type will you be using?", true, cancellationToken).ConfigureAwait(false);

				var databaseConfiguration = new DatabaseConfiguration();
				do
				{
					await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Please enter one of {0}, {1}, or {2}: ", DatabaseType.MariaDB, DatabaseType.SqlServer, DatabaseType.MySql), false, cancellationToken).ConfigureAwait(false);
					var databaseTypeString = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
					if (Enum.TryParse<DatabaseType>(databaseTypeString, out var databaseType))
					{
						databaseConfiguration.DatabaseType = databaseType;
						break;
					}

					await console.WriteAsync("Invalid database type!", true, cancellationToken).ConfigureAwait(false);
				}
				while (true);

				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync("Enter the server's address and port (blank for local): ", false, cancellationToken).ConfigureAwait(false);
				var serverAddress = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
				var serverPort = 3306U;
				if (String.IsNullOrWhiteSpace(serverAddress))
					serverAddress = null;
				else
				{
					var m = Regex.Match(serverAddress, @"^(?<server>.+):(?<port>[0-9]+)$");
					if (m.Success)
					{
						serverAddress = m.Groups["server"].Value;
						serverPort = uint.Parse(m.Groups["port"].Value, CultureInfo.InvariantCulture);
					}
				}

				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync("Enter the database name (Can be from previous installation. Otherwise, should not exist): ", false, cancellationToken).ConfigureAwait(false);
				string databaseName;
				do
				{
					databaseName = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
					if (!String.IsNullOrWhiteSpace(databaseName))
						break;
					await console.WriteAsync("Invalid database name!", true, cancellationToken).ConfigureAwait(false);
				}
				while (true);

				var dbExists = await PromptYesNo("Does this database already exist? (y/n): ", cancellationToken).ConfigureAwait(false);

				bool useWinAuth;
				if (databaseConfiguration.DatabaseType == DatabaseType.SqlServer && platformIdentifier.IsWindows)
					useWinAuth = await PromptYesNo("Use Windows Authentication? (y/n): ", cancellationToken).ConfigureAwait(false);
				else
					useWinAuth = false;

				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);

				string username = null;
				string password = null;
				if (!useWinAuth)
				{
					await console.WriteAsync("Enter username: ", false, cancellationToken).ConfigureAwait(false);
					username = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
					await console.WriteAsync("Enter password: ", false, cancellationToken).ConfigureAwait(false);
					password = await console.ReadLineAsync(true, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					await console.WriteAsync("IMPORTANT: If using the service runner, ensure this computer's LocalSystem account has CREATE DATABASE permissions on the target server!", true, cancellationToken).ConfigureAwait(false);
					await console.WriteAsync("The account it uses in MSSQL is usually \"NT AUTHORITY\\SYSTEM\" and the role it needs is usually \"dbcreator\".", true, cancellationToken).ConfigureAwait(false);
					await console.WriteAsync("We'll run a sanity test here, but it won't be indicative of the service's permissions if that is the case", true, cancellationToken).ConfigureAwait(false);
				}

				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);

				DbConnection testConnection;
				void CreateTestConnection(string connectionString)
				{
					testConnection = dbConnectionFactory.CreateConnection(connectionString, databaseConfiguration.DatabaseType);
				}

				if (databaseConfiguration.DatabaseType == DatabaseType.SqlServer)
				{
					var csb = new SqlConnectionStringBuilder
					{
						ApplicationName = application.VersionPrefix,
						DataSource = serverAddress ?? "(local)"
					};
					if (useWinAuth)
						csb.IntegratedSecurity = true;
					else
					{
						csb.UserID = username;
						csb.Password = password;
					}

					CreateTestConnection(csb.ConnectionString);
					csb.InitialCatalog = databaseName;
					databaseConfiguration.ConnectionString = csb.ConnectionString;
				}
				else
				{
					var csb = new MySqlConnectionStringBuilder
					{
						Server = serverAddress ?? "127.0.0.1",
						Port = serverPort,
						UserID = username,
						Password = password
					};

					CreateTestConnection(csb.ConnectionString);
					csb.Database = databaseName;
					databaseConfiguration.ConnectionString = csb.ConnectionString;
				}

				try
				{
					using (testConnection)
					{
						await console.WriteAsync("Testing connection...", true, cancellationToken).ConfigureAwait(false);
						await testConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
						await console.WriteAsync("Connection successful!", true, cancellationToken).ConfigureAwait(false);

						if (databaseConfiguration.DatabaseType != DatabaseType.SqlServer)
						{
							await console.WriteAsync("Checking MySQL/MariaDB version...", true, cancellationToken).ConfigureAwait(false);
							using (var command = testConnection.CreateCommand())
							{
								command.CommandText = "SELECT VERSION()";
								var fullVersion = (string)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
								await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Found {0}", fullVersion), true, cancellationToken).ConfigureAwait(false);
								var splits = fullVersion.Split('-');
								databaseConfiguration.MySqlServerVersion = splits[0];
							}
						}

						if (!dbExists)
						{
							await console.WriteAsync("Testing create DB permission...", true, cancellationToken).ConfigureAwait(false);
							using (var command = testConnection.CreateCommand())
							{
								command.CommandText = String.Format(CultureInfo.InvariantCulture, "CREATE DATABASE {0}", databaseName);
								await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
							}

							await console.WriteAsync("Success!", true, cancellationToken).ConfigureAwait(false);
							await console.WriteAsync("Dropping test database...", true, cancellationToken).ConfigureAwait(false);
							using (var command = testConnection.CreateCommand())
							{
								command.CommandText = String.Format(CultureInfo.InvariantCulture, "DROP DATABASE {0}", databaseName);
								try
								{
									await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
								}
								catch (OperationCanceledException)
								{
									throw;
								}
								catch (Exception e)
								{
									await console.WriteAsync(e.Message, true, cancellationToken).ConfigureAwait(false);
									await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
									await console.WriteAsync("This should be okay, but you may want to manually drop the database before continuing!", true, cancellationToken).ConfigureAwait(false);
									await console.WriteAsync("Press any key to continue...", true, cancellationToken).ConfigureAwait(false);
									await console.PressAnyKeyAsync(cancellationToken).ConfigureAwait(false);
								}
							}
						}
					}

					return databaseConfiguration;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception e)
				{
					await console.WriteAsync(e.Message, true, cancellationToken).ConfigureAwait(false);
					await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
					await console.WriteAsync("Retrying database configuration...", true, cancellationToken).ConfigureAwait(false);
				}
			}
			while (true);
		}

		/// <summary>
		/// Prompts the user to create a <see cref="GeneralConfiguration"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the new <see cref="GeneralConfiguration"/></returns>
		async Task<GeneralConfiguration> ConfigureGeneral(CancellationToken cancellationToken)
		{
			var newGeneralConfiguration = new GeneralConfiguration
			{
				SetupWizardMode = SetupWizardMode.Never
			};

			do
			{
				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Minimum database user password length (leave blank for default of {0}): ", newGeneralConfiguration.MinimumPasswordLength), false, cancellationToken).ConfigureAwait(false);
				var passwordLengthString = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
				if (String.IsNullOrWhiteSpace(passwordLengthString))
					break;
				if (UInt32.TryParse(passwordLengthString, out var passwordLength) && passwordLength >= 0)
				{
					newGeneralConfiguration.MinimumPasswordLength = passwordLength;
					break;
				}

				await console.WriteAsync("Please enter a positive integer!", true, cancellationToken).ConfigureAwait(false);
			}
			while (true);

			do
			{
				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Timeout for sending and receiving BYOND topics (ms, 0 for infinite, leave blank for default of {0}): ", newGeneralConfiguration.ByondTopicTimeout), false, cancellationToken).ConfigureAwait(false);
				var topicTimeoutString = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
				if (String.IsNullOrWhiteSpace(topicTimeoutString))
					break;
				if (Int32.TryParse(topicTimeoutString, out var topicTimeout) && topicTimeout >= 0)
				{
					newGeneralConfiguration.ByondTopicTimeout = topicTimeout;
					break;
				}

				await console.WriteAsync("Please enter a positive integer!", true, cancellationToken).ConfigureAwait(false);
			}
			while (true);

			await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
			await console.WriteAsync("Enter a GitHub personal access token to bypass some rate limits (this is optional and does not require any scopes)", true, cancellationToken).ConfigureAwait(false);
			await console.WriteAsync("GitHub personal access token: ", false, cancellationToken).ConfigureAwait(false);
			newGeneralConfiguration.GitHubAccessToken = await console.ReadLineAsync(true, cancellationToken).ConfigureAwait(false);
			if (String.IsNullOrWhiteSpace(newGeneralConfiguration.GitHubAccessToken))
				newGeneralConfiguration.GitHubAccessToken = null;
			return newGeneralConfiguration;
		}

		/// <summary>
		/// Prompts the user to create a <see cref="FileLoggingConfiguration"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the new <see cref="FileLoggingConfiguration"/></returns>
		async Task<FileLoggingConfiguration> ConfigureLogging(CancellationToken cancellationToken)
		{
			var fileLoggingConfiguration = new FileLoggingConfiguration();
			await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
			fileLoggingConfiguration.Disable = !await PromptYesNo("Enable file logging? (y/n): ", cancellationToken).ConfigureAwait(false);

			if (!fileLoggingConfiguration.Disable)
			{
				do
				{
					await console.WriteAsync("Log file directory path (leave blank for default): ", false, cancellationToken).ConfigureAwait(false);
					fileLoggingConfiguration.Directory = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
					if (String.IsNullOrWhiteSpace(fileLoggingConfiguration.Directory))
					{
						fileLoggingConfiguration.Directory = null;
						break;
					}

					// test a write of it
					await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
					await console.WriteAsync("Testing directory access...", true, cancellationToken).ConfigureAwait(false);
					try
					{
						await ioManager.CreateDirectory(fileLoggingConfiguration.Directory, cancellationToken).ConfigureAwait(false);
						var testFile = ioManager.ConcatPath(fileLoggingConfiguration.Directory, String.Format(CultureInfo.InvariantCulture, "WizardAccesTest.{0}.deleteme", Guid.NewGuid()));
						await ioManager.WriteAllBytes(testFile, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
						try
						{
							await ioManager.DeleteFile(testFile, cancellationToken).ConfigureAwait(false);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception e)
						{
							await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Error deleting test log file: {0}", testFile), true, cancellationToken).ConfigureAwait(false);
							await console.WriteAsync(e.Message, true, cancellationToken).ConfigureAwait(false);
							await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
						}

						break;
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception e)
					{
						await console.WriteAsync(e.Message, true, cancellationToken).ConfigureAwait(false);
						await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
						await console.WriteAsync("Please verify the path is valid and you have access to it!", true, cancellationToken).ConfigureAwait(false);
					}
				}
				while (true);

				async Task<LogLevel?> PromptLogLevel(string question)
				{
					do
					{
						await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
						await console.WriteAsync(question, true, cancellationToken).ConfigureAwait(false);
						await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Enter one of {0}/{1}/{2}/{3}/{4}/{5} (leave blank for default): ", nameof(LogLevel.Trace), nameof(LogLevel.Debug), nameof(LogLevel.Information), nameof(LogLevel.Warning), nameof(LogLevel.Error), nameof(LogLevel.Critical)), false, cancellationToken).ConfigureAwait(false);
						var responseString = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
						if (String.IsNullOrWhiteSpace(responseString))
							return null;
						if (Enum.TryParse<LogLevel>(responseString, out var logLevel) && logLevel != LogLevel.None)
							return logLevel;
						await console.WriteAsync("Invalid log level!", true, cancellationToken).ConfigureAwait(false);
					}
					while (true);
				}

				fileLoggingConfiguration.LogLevel = await PromptLogLevel(String.Format(CultureInfo.InvariantCulture, "Enter the level limit for normal logs (default {0}).", fileLoggingConfiguration.LogLevel)).ConfigureAwait(false) ?? fileLoggingConfiguration.LogLevel;
				fileLoggingConfiguration.MicrosoftLogLevel = await PromptLogLevel(String.Format(CultureInfo.InvariantCulture, "Enter the level limit for Microsoft logs (VERY verbose, default {0}).", fileLoggingConfiguration.MicrosoftLogLevel)).ConfigureAwait(false) ?? fileLoggingConfiguration.MicrosoftLogLevel;
			}

			return fileLoggingConfiguration;
		}

		/// <summary>
		/// Prompts the user to create a <see cref="ControlPanelConfiguration"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the new <see cref="ControlPanelConfiguration"/></returns>
		async Task<ControlPanelConfiguration> ConfigureControlPanel(CancellationToken cancellationToken)
		{
			var config = new ControlPanelConfiguration
			{
				Enable = await PromptYesNo("Enable the web control panel? (y/n): ", cancellationToken).ConfigureAwait(false),
				AllowAnyOrigin = await PromptYesNo("Allow web control panels hosted elsewhere to access the server? (Access-Control-Allow-Origin: *) (y/n): ", cancellationToken).ConfigureAwait(false)
			};

			if (!config.AllowAnyOrigin)
			{
				await console.WriteAsync("Enter a comma seperated list of CORS allowed origins (optional): ", false, cancellationToken).ConfigureAwait(false);
				var commaSeperatedOrigins = await console.ReadLineAsync(false, cancellationToken).ConfigureAwait(false);
				if (!String.IsNullOrWhiteSpace(commaSeperatedOrigins))
				{
					var splits = commaSeperatedOrigins.Split(',');
					config.AllowedOrigins = new List<string>(splits.Select(x => x.Trim()));
				}
			}

			return config;
		}

		/// <summary>
		/// Saves a given <see cref="Configuration"/> set to <paramref name="userConfigFileName"/>
		/// </summary>
		/// <param name="userConfigFileName">The file to save the <see cref="Configuration"/> to</param>
		/// <param name="hostingPort">The hosting port to save</param>
		/// <param name="databaseConfiguration">The <see cref="DatabaseConfiguration"/> to save</param>
		/// <param name="newGeneralConfiguration">The <see cref="GeneralConfiguration"/> to save</param>
		/// <param name="fileLoggingConfiguration">The <see cref="FileLoggingConfiguration"/> to save</param>
		/// <param name="controlPanelConfiguration">The <see cref="ControlPanelConfiguration"/> to save</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task SaveConfiguration(string userConfigFileName, ushort? hostingPort, DatabaseConfiguration databaseConfiguration, GeneralConfiguration newGeneralConfiguration, FileLoggingConfiguration fileLoggingConfiguration, ControlPanelConfiguration controlPanelConfiguration, CancellationToken cancellationToken)
		{
			await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "Configuration complete! Saving to {0}", userConfigFileName), true, cancellationToken).ConfigureAwait(false);

			var map = new Dictionary<string, object>()
			{
				{ DatabaseConfiguration.Section, databaseConfiguration },
				{ GeneralConfiguration.Section, newGeneralConfiguration },
				{ FileLoggingConfiguration.Section, fileLoggingConfiguration },
				{ ControlPanelConfiguration.Section, controlPanelConfiguration }
			};

			if (hostingPort.HasValue)
				map.Add("Kestrel", new
				{
					EndPoints = new
					{
						Http = new
						{
							Url = String.Format(CultureInfo.InvariantCulture, "http://0.0.0.0:{0}", hostingPort)
						}
					}
				});

			var json = JsonConvert.SerializeObject(map, Formatting.Indented);
			var configBytes = Encoding.UTF8.GetBytes(json);

			try
			{
				await ioManager.WriteAllBytes(userConfigFileName, configBytes, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				await console.WriteAsync(e.Message, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync("For your convienence, here's the json we tried to write out:", true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync(json, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
				await console.WriteAsync("Press any key to exit...", true, cancellationToken).ConfigureAwait(false);
				await console.PressAnyKeyAsync(cancellationToken).ConfigureAwait(false);
				throw new OperationCanceledException();
			}

			await console.WriteAsync("Waiting for configuration changes to reload...", true, cancellationToken).ConfigureAwait(false);

			// we need to wait for the configuration's file system watcher to read and reload the changes
			await asyncDelayer.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Runs the <see cref="SetupWizard"/>
		/// </summary>
		/// <param name="userConfigFileName">The path to the settings json to build</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task RunWizard(string userConfigFileName, CancellationToken cancellationToken)
		{
			// welcome message
			await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);
			await console.WriteAsync("Welcome to tgstation-server 4!", true, cancellationToken).ConfigureAwait(false);
			await console.WriteAsync("This wizard will help you configure your server.", true, cancellationToken).ConfigureAwait(false);

			var hostingPort = await PromptForHostingPort(cancellationToken).ConfigureAwait(false);

			var databaseConfiguration = await ConfigureDatabase(cancellationToken).ConfigureAwait(false);

			var newGeneralConfiguration = await ConfigureGeneral(cancellationToken).ConfigureAwait(false);

			var fileLoggingConfiguration = await ConfigureLogging(cancellationToken).ConfigureAwait(false);

			var controlPanelConfiguration = await ConfigureControlPanel(cancellationToken).ConfigureAwait(false);

			await console.WriteAsync(null, true, cancellationToken).ConfigureAwait(false);

			await SaveConfiguration(userConfigFileName, hostingPort, databaseConfiguration, newGeneralConfiguration, fileLoggingConfiguration, controlPanelConfiguration, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<bool> CheckRunWizard(CancellationToken cancellationToken)
		{
			var setupWizardMode = generalConfiguration.SetupWizardMode;
			logger.LogTrace("Checking if setup wizard should run. SetupWizardMode: {0}", setupWizardMode);

			if (setupWizardMode == SetupWizardMode.Never)
			{
				logger.LogTrace("Skipping due to configuration...");
				return false;
			}

			var forceRun = setupWizardMode == SetupWizardMode.Force || setupWizardMode == SetupWizardMode.Only;
			if (!console.Available)
			{
				if (forceRun)
					throw new InvalidOperationException("Asked to run setup wizard with no console avaliable!");
				logger.LogTrace("Skipping due to console not being available...");
				return false;
			}

			var userConfigFileName = String.Format(CultureInfo.InvariantCulture, "appsettings.{0}.json", hostingEnvironment.EnvironmentName);
			var exists = await ioManager.FileExists(userConfigFileName, cancellationToken).ConfigureAwait(false);

			bool shouldRunBasedOnAutodetect;
			if (exists)
			{
				var bytes = await ioManager.ReadAllBytes(userConfigFileName, cancellationToken).ConfigureAwait(false);
				var contents = Encoding.UTF8.GetString(bytes);
				var existingConfigIsEmpty = String.IsNullOrWhiteSpace(contents) || contents.Trim() == "{}";
				logger.LogTrace("Configuration json detected. Empty: {0}", existingConfigIsEmpty);
				shouldRunBasedOnAutodetect = existingConfigIsEmpty;
			}
			else
			{
				shouldRunBasedOnAutodetect = true;
				logger.LogTrace("No configuration json detected");
			}

			if (!shouldRunBasedOnAutodetect)
			{
				if (forceRun)
				{
					logger.LogTrace("Asking user to bypass due to force run request...");
					await console.WriteAsync(String.Format(CultureInfo.InvariantCulture, "The configuration settings are requesting the setup wizard be run, but you already appear to have a configuration file ({0})!", userConfigFileName), true, cancellationToken).ConfigureAwait(false);

					forceRun = await PromptYesNo("Continue running setup wizard? (y/n): ", cancellationToken).ConfigureAwait(false);
				}

				if (!forceRun)
					return false;
			}

			// flush the logs to prevent console conflicts
			await asyncDelayer.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

			await RunWizard(userConfigFileName, cancellationToken).ConfigureAwait(false);
			return true;
		}
	}
}
