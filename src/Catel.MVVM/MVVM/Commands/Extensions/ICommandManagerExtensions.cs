﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CommandManagerExtensions.cs" company="Catel development team">
//   Copyright (c) 2008 - 2015 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

#if !XAMARIN && !XAMARIN_FORMS

namespace Catel
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Windows.Input;
    using Catel;
    using Catel.IoC;
    using Catel.Logging;
    using Catel.MVVM;
    using Catel.Reflection;
    using InputGesture = Catel.Windows.Input.InputGesture;

    /// <summary>
    /// Extension methods for the <see cref="ICommandManager"/>.
    /// </summary>
    public static partial class ICommandManagerExtensions
    {
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Finds the commands inside the <see cref="ICommandManager"/> by gesture.
        /// </summary>
        /// <param name="commandManager">The command manager.</param>
        /// <param name="inputGesture">The input gesture.</param>
        /// <returns>Dictionary&lt;System.String, ICommand&gt;.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="commandManager"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="inputGesture"/> is <c>null</c>.</exception>
        public static Dictionary<string, ICommand> FindCommandsByGesture(this ICommandManager commandManager, InputGesture inputGesture)
        {
            Argument.IsNotNull("commandManager", commandManager);
            Argument.IsNotNull("inputGesture", inputGesture);

            var commands = new Dictionary<string, ICommand>();

            foreach (var commandName in commandManager.GetCommands())
            {
                var commandInputGesture = commandManager.GetInputGesture(commandName);
                if (inputGesture.Equals(commandInputGesture))
                {
                    commands[commandName] = commandManager.GetCommand(commandName);
                }
            }

            return commands;
        }

        /// <summary>
        /// Creates a command using a naming convention with the specified gesture.
        /// </summary>
        /// <param name="commandManager">The command manager.</param>
        /// <param name="containerType">Type of the container.</param>
        /// <param name="commandNameFieldName">Name of the command name field.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="commandManager"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="containerType"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="commandNameFieldName"/> is <c>null</c>.</exception>
        public static void CreateCommandWithGesture(this ICommandManager commandManager, Type containerType, string commandNameFieldName)
        {
            Argument.IsNotNull("commandManager", commandManager);
            Argument.IsNotNull("containerType", containerType);
            Argument.IsNotNullOrWhitespace("commandNameFieldName", commandNameFieldName);

            Log.Debug("Creating command '{0}'", commandNameFieldName);

            // Note: we must store bindingflags inside variable otherwise invalid IL will be generated
            var bindingFlags = BindingFlags.Public | BindingFlags.Static;
            var commandNameField = containerType.GetFieldEx(commandNameFieldName, bindingFlags);
            if (commandNameField is null)
            {
                throw Log.ErrorAndCreateException<InvalidOperationException>("Command '{0}' is not available on container type '{1}'",
                    commandNameFieldName, containerType.GetSafeFullName(false));
            }

            var commandName = (string)commandNameField.GetValue(null);
            if (commandManager.IsCommandCreated(commandName))
            {
                Log.Debug("Command '{0}' is already created, skipping...", commandName);
                return;
            }

            InputGesture commandInputGesture = null;
            var inputGestureField = containerType.GetFieldEx(string.Format("{0}InputGesture", commandNameFieldName), bindingFlags);
            if (inputGestureField is not null)
            {
                commandInputGesture = inputGestureField.GetValue(null) as InputGesture;
            }

            commandManager.CreateCommand(commandName, commandInputGesture);

            var commandContainerName = string.Format("{0}CommandContainer", commandName.Replace(".", string.Empty));

            // https://github.com/Catel/Catel/issues/1383: CommandManager.CreateCommandWithGesture does not create CommandContainer
            var commandContainerType = (from type in TypeCache.GetTypes(allowInitialization: true)
                                        where string.Equals(type.Name, commandContainerName, StringComparison.OrdinalIgnoreCase)
                                        select type).FirstOrDefault();
            if (commandContainerType is null)
            {
                Log.Debug("Couldn't find command container '{0}', you will need to add a custom action or command manually in order to make the CompositeCommand useful", commandContainerName);
                return;
            }

            Log.Debug("Found command container '{0}', registering it in the ServiceLocator now", commandContainerType.GetSafeFullName(false));

#pragma warning disable IDISP001 // Dispose created.
            var serviceLocator = commandManager.GetServiceLocator();
#pragma warning restore IDISP001 // Dispose created.

            if (!serviceLocator.IsTypeRegistered(commandContainerType))
            {
                var typeFactory = serviceLocator.ResolveType<ITypeFactory>();
                var commandContainer = typeFactory.CreateInstance(commandContainerType);
                if (commandContainer is not null)
                {
                    serviceLocator.RegisterInstance(commandContainerType, commandContainer);
                }
                else
                {
                    Log.Warning("Cannot create command container '{0}', skipping registration", commandContainerType.GetSafeFullName(false));
                }
            }
        }
    }
}

#endif
