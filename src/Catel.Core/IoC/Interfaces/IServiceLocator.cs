﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IServiceLocator.cs" company="Catel development team">
//   Copyright (c) 2008 - 2015 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel.IoC
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Available registration types.
    /// </summary>
    public enum RegistrationType
    {
        /// <summary>
        /// Singleton mode which means that the same instance will be returned every time a type is resolved.
        /// </summary>
        Singleton,

        /// <summary>
        /// Transient mode which means that a new instance will be returned every time a type is resolved.
        /// </summary>
        Transient
    }

    /// <summary>
    /// The service locator which is used to retrieve the right instances of services.
    /// <para />
    /// The cool thing about this service locator is that it can use external containers (from example from Unity)
    /// to resolve types if the types are not registered in the container itself. To do this, use the following code:
    /// <para />
    /// <code>
    ///   var serviceLocator = ServiceLocator.Default;
    ///   serviceLocator.RegisterExternalContainer(myUnityContainer);
    /// </code>
    /// <para />
    /// The service locator will use the external containers in case the current container does not contain the
    /// type. If the external containers also don't contain the type, there is one last way to resolve the type
    /// using the <see cref="MissingType"/> event. The event passes <see cref="MissingTypeEventArgs"/> that contains
    /// the type the service locator is looking for. By setting the <see cref="MissingTypeEventArgs.ImplementingInstance"/> or 
    /// <see cref="MissingTypeEventArgs.ImplementingType"/> in the handler, the service locator will resolve the type.
    /// </summary>
    public interface IServiceLocator : IServiceProvider, IDisposable
    {
        #region Properties
        /// <summary>
        /// Gets or sets a value indicating whether the service locator can resolve non abstract types without registration.
        /// </summary>
        bool CanResolveNonAbstractTypesWithoutRegistration { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this service locators will automatically register types via attributes.
        /// </summary>
        /// <remarks>
        /// By default, this value is <c>true</c>.
        /// </remarks>
        bool AutoRegisterTypesViaAttributes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this service locators will ignore incorrect usage of <see cref="ServiceLocatorRegistrationAttribute"/> and do not throw <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <remarks>
        /// By default, this value is <c>true</c>.
        /// </remarks>
        bool IgnoreRuntimeIncorrectUsageOfRegisterAttribute { get; set; }
        #endregion

        #region Events
        /// <summary>
        /// Occurs when a type cannot be resolved the by service locator. It first tries to raise this event.
        /// <para />
        /// If there are no handlers or no handler can fill up the missing type, an exception will be thrown by
        /// the service locator.
        /// </summary>
        event EventHandler<MissingTypeEventArgs> MissingType;

        /// <summary>
        /// Occurs when a type is registered in the service locator.
        /// </summary>
        event EventHandler<TypeRegisteredEventArgs> TypeRegistered;

        /// <summary>
        /// Occurs when a type is unregistered in the service locator.
        /// </summary>
        event EventHandler<TypeUnregisteredEventArgs> TypeUnregistered;

        /// <summary>
        /// Occurs when a type is instantiated in the service locator.
        /// </summary>
        event EventHandler<TypeInstantiatedEventArgs> TypeInstantiated;
        #endregion

        #region Methods
        /// <summary>
        /// Gets the registration info about the specified type.
        /// </summary>
        /// <param name="serviceType">Type of the service.</param>
        /// <param name="tag">The tag the service is registered with. The default value is <c>null</c>.</param>
        /// <returns>The <see cref="RegistrationInfo" /> or <c>null</c> if the type is not registered.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="serviceType" /> is <c>null</c>.</exception>
        RegistrationInfo GetRegistrationInfo(Type serviceType, object tag = null);

        /// <summary>
        /// Determines whether the specified service type is registered.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <returns><c>true</c> if the specified service type is registered; otherwise, <c>false</c>.</returns>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="serviceType"/> is <c>null</c>.</exception>
        bool IsTypeRegistered(Type serviceType, object tag = null);

        /// <summary>
        /// Determines whether the specified service type is registered as singleton.
        /// </summary>
        /// <param name="serviceType">The service type.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <returns><c>true</c> if the <paramref name="serviceType" /> type is registered as singleton, otherwise <c>false</c>.</returns>
        bool IsTypeRegisteredAsSingleton(Type serviceType, object tag = null);

        /// <summary>
        /// Determines whether the specified service type is registered with or without tag.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <returns><c>true</c> if the specified service type is registered; otherwise, <c>false</c>.</returns>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        /// <exception cref="ArgumentNullException">The <paramref name="serviceType"/> is <c>null</c>.</exception>
        bool IsTypeRegisteredWithOrWithoutTag(Type serviceType);

        /// <summary>
        /// Registers a specific instance of a service. 
        /// </summary>
        /// <param name="serviceType">Type of the service.</param>
        /// <param name="instance">The instance.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="serviceType"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="instance"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The <paramref name="instance"/> is not of the right type.</exception>
        void RegisterInstance(Type serviceType, object instance, object tag = null);

        /// <summary>
        /// Registers an implementation of a service, but only if the type is not yet registered.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <param name="serviceImplementationType">The type of the implementation.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <param name="registrationType">The registration type. The default value is <see cref="RegistrationType.Singleton" />.</param>
        /// <param name="registerIfAlreadyRegistered">If set to <c>true</c>, an older type registration is overwritten by this new one.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="serviceType" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="serviceImplementationType" /> is <c>null</c>.</exception>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        void RegisterType(Type serviceType, Type serviceImplementationType, object tag = null, RegistrationType registrationType = RegistrationType.Singleton, bool registerIfAlreadyRegistered = true);

        /// <summary>
        /// Registers an implementation of a service using a create type callback, but only if the type is not yet registered.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <param name="createServiceFunc">The create service function.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <param name="registrationType">The registration type. The default value is <see cref="RegistrationType.Singleton" />.</param>
        /// <param name="registerIfAlreadyRegistered">If set to <c>true</c>, an older type registration is overwritten by this new one.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="serviceType" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="createServiceFunc" /> is <c>null</c>.</exception>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        void RegisterType(Type serviceType, Func<ITypeFactory, ServiceLocatorRegistration, object> createServiceFunc, object tag = null, RegistrationType registrationType = RegistrationType.Singleton, bool registerIfAlreadyRegistered = true);

        /// <summary>
        /// Resolves an instance of the type registered on the service.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <returns>An instance of the type registered on the service.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="serviceType" /> is <c>null</c>.</exception>
        /// <exception cref="TypeNotRegisteredException">The type is not found in any container.</exception>
        /// <remarks>
        /// Note that the actual implementation lays in the hands of the IoC technique being used.
        /// </remarks>
        object ResolveType(Type serviceType, object tag = null);

        /// <summary>
        /// Resolves the type but forces the use of a specific type factory, which is required for nested service locators.
        /// </summary>
        /// <param name="typeFactory">The <see cref="ITypeFactory"/> type factory to use when this type needs construction.</param>
        /// <param name="serviceType">The type of the service.</param>
        /// <param name="tag">The tag to register the service with. The default value is <c>null</c>.</param>
        /// <returns>An instance of the type registered on the service.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="serviceType" /> is <c>null</c>.</exception>
        /// <exception cref="TypeNotRegisteredException">The type is not found in any container.</exception>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        object ResolveTypeUsingFactory(ITypeFactory typeFactory, Type serviceType, object tag = null);

        /// <summary>
        /// Resolves all instances of the type registered on the service.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <returns>All instance of the type registered on the service.</returns>
        /// <exception cref="System.ArgumentNullException">The <paramref name="serviceType" /> is <c>null</c>.</exception>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        IEnumerable<object> ResolveTypes(Type serviceType);

        /// <summary>
        /// Resolves all instances of the type registered on the service.
        /// </summary>
        /// <param name="typeFactory">The <see cref="ITypeFactory"/> type factory to use when this type needs construction.</param>
        /// <param name="serviceType">The type of the service.</param>
        /// <returns>All instance of the type registered on the service.</returns>
        /// <exception cref="System.ArgumentNullException">The <paramref name="serviceType" /> is <c>null</c>.</exception>
        /// <remarks>Note that the actual implementation lays in the hands of the IoC technique being used.</remarks>
        IEnumerable<object> ResolveTypesUsingFactory(ITypeFactory typeFactory, Type serviceType);
        #endregion

        /// <summary>
        /// Determines whether all the specified types are registered with the service locator.
        /// </summary>
        /// <remarks>
        /// Note that this method is written for optimalization by the <see cref="TypeFactory"/>. This means that the 
        /// <see cref="TypeFactory"/> does not need to call the <see cref="ServiceLocator"/> several times to construct
        /// a single type using dependency injection.
        /// <para />
        /// Only use this method if you know what you are doing, otherwise use the <see cref="ServiceLocator.IsTypeRegistered"/> instead.
        /// </remarks>
        /// <param name="types">The types that should be registered.</param>
        /// <returns><c>true</c> if all the specified types are registered with this instance of the <see cref="IServiceLocator" />; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentException">The <paramref name="types"/> is <c>null</c> or an empty array.</exception>
        bool AreMultipleTypesRegistered(params Type[] types);

        /// <summary>
        /// Resolves all the specified types.
        /// </summary>
        /// <remarks>
        /// Note that this method is written for optimalization by the <see cref="TypeFactory"/>. This means that the 
        /// <see cref="TypeFactory"/> does not need to call the <see cref="ServiceLocator"/> several times to construct
        /// a single type using dependency injection.
        /// <para />
        /// Only use this method if you know what you are doing, otherwise use the <see cref="ServiceLocator.IsTypeRegistered"/> instead.
        /// </remarks>
        /// <param name="types">The collection of types that should be resolved.</param>
        /// <returns>The resolved types in the same order as the types.</returns>
        /// <exception cref="ArgumentException">The <paramref name="types"/> is <c>null</c> or an empty array.</exception>
        object[] ResolveMultipleTypes(params Type[] types);

        /// <summary>
        /// Removes the registered type with the specific tag.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <param name="tag">The tag of the registered the service. The default value is <c>null</c>.</param>
        /// <returns><c>true</c> if the type was removed; otherwise <c>false</c>.</returns>
        /// <exception cref="System.ArgumentNullException">The <paramref name="serviceType"/> is <c>null</c>.</exception>
        bool RemoveType(Type serviceType, object tag = null);

        /// <summary>
        /// Removes all registered types of a certain service type.
        /// </summary>
        /// <param name="serviceType">The type of the service.</param>
        /// <returns><c>true</c> if the type was removed; otherwise <c>false</c>.</returns>
        /// <exception cref="System.ArgumentNullException">The <paramref name="serviceType"/> is <c>null</c>.</exception>
        bool RemoveAllTypes(Type serviceType);
    }
}
