﻿// <copyright file="MapBoxGeocoding.cs" company="Geo.NET">
// Copyright (c) Geo.NET.
// Licensed under the MIT license. See the LICENSE file in the solution root for full license information.
// </copyright>

namespace Geo.MapBox.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using Geo.Core;
    using Geo.MapBox.Abstractions;
    using Geo.MapBox.Enums;
    using Geo.MapBox.Models;
    using Geo.MapBox.Models.Exceptions;
    using Geo.MapBox.Models.Parameters;
    using Geo.MapBox.Models.Responses;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A service to call the MapBox geocoding API.
    /// </summary>
    public class MapBoxGeocoding : ClientExecutor, IMapBoxGeocoding
    {
        private const string _apiName = "MapBox";
        private readonly string _geocodeUri = "https://api.mapbox.com/geocoding/v5/{0}/{1}.json";
        private readonly string _reverseGeocodeUri = "https://api.mapbox.com/geocoding/v5/{0}/{1}.json";
        private readonly string _placesEndpoint = "mapbox.places";
        private readonly string _permanentEndpoint = "mapbox.places-permanent";
        private readonly IMapBoxKeyContainer _keyContainer;
        private readonly IStringLocalizer<MapBoxGeocoding> _localizer;
        private readonly ILogger<MapBoxGeocoding> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MapBoxGeocoding"/> class.
        /// </summary>
        /// <param name="client">A <see cref="HttpClient"/> used for placing calls to the here Geocoding API.</param>
        /// <param name="keyContainer">A <see cref="IMapBoxKeyContainer"/> used for fetching the here key.</param>
        /// <param name="localizer">A <see cref="IStringLocalizer{T}"/> used for localizing log or exception messages.</param>
        /// <param name="coreLocalizer">A <see cref="IStringLocalizer{T}"/> used for localizing core log or exception messages.</param>
        /// <param name="logger">A <see cref="ILogger{T}"/> used for logging information.</param>
        public MapBoxGeocoding(
            HttpClient client,
            IMapBoxKeyContainer keyContainer,
            IStringLocalizer<MapBoxGeocoding> localizer,
            IStringLocalizer<ClientExecutor> coreLocalizer,
            ILogger<MapBoxGeocoding> logger = null)
            : base(client, coreLocalizer)
        {
            _keyContainer = keyContainer;
            _localizer = localizer;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Response<List<string>>> GeocodingAsync(
            GeocodingParameters parameters,
            CancellationToken cancellationToken = default)
        {
            var uri = ValidateAndBuildUri<GeocodingParameters>(parameters, BuildGeocodingRequest);

            return await CallAsync<Response<List<string>>, MapBoxException>(uri, _apiName, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<Response<Coordinate>> ReverseGeocodingAsync(
            ReverseGeocodingParameters parameters,
            CancellationToken cancellationToken = default)
        {
            var uri = ValidateAndBuildUri<ReverseGeocodingParameters>(parameters, BuildReverseGeocodingRequest);

            return await CallAsync<Response<Coordinate>, MapBoxException>(uri, _apiName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates the uri and builds it based on the parameter type.
        /// </summary>
        /// <typeparam name="TParameters">The type of the parameters.</typeparam>
        /// <param name="parameters">The parameters to validate and create a uri from.</param>
        /// <param name="uriBuilderFunction">The method to use to create the uri.</param>
        /// <returns>A <see cref="Uri"/> with the uri crafted from the parameters.</returns>
        internal Uri ValidateAndBuildUri<TParameters>(TParameters parameters, Func<TParameters, Uri> uriBuilderFunction)
        {
            if (parameters is null)
            {
                var error = _localizer["Null Parameters"];
                _logger?.LogError(error);
                throw new MapBoxException(error, new ArgumentNullException(nameof(parameters)));
            }

            try
            {
                return uriBuilderFunction(parameters);
            }
            catch (ArgumentException ex)
            {
                var error = _localizer["Failed To Create Uri"];
                _logger?.LogError(error);
                throw new MapBoxException(error, ex);
            }
        }

        /// <summary>
        /// Builds the geocoding uri based on the passed parameters.
        /// </summary>
        /// <param name="parameters">A <see cref="GeocodingParameters"/> with the geocoding parameters to build the uri with.</param>
        /// <returns>A <see cref="Uri"/> with the completed MapBox geocoding uri.</returns>
        /// <exception cref="ArgumentException">Thrown when the 'Query' parameter and the 'QualifiedQuery' parameter are null or invalid.</exception>
        internal Uri BuildGeocodingRequest(GeocodingParameters parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters.Query))
            {
                var error = _localizer["Invalid Query"];
                _logger?.LogError(error);
                throw new ArgumentException(error, nameof(parameters.Query));
            }

            var uriBuilder = new UriBuilder(string.Format(CultureInfo.InvariantCulture, _geocodeUri, parameters.EndpointType == EndpointType.Places ? _placesEndpoint : _permanentEndpoint, parameters.Query));
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);

#pragma warning disable CA1308 // Normalize strings to uppercase
            query.Add("autocomplete", parameters.ReturnAutocomplete.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
#pragma warning restore CA1308 // Normalize strings to uppercase

            if (parameters.BoundingBox != null)
            {
                query.Add("bbox", parameters.BoundingBox.ToString());
            }
            else
            {
                _logger?.LogDebug(_localizer["Invalid Bounding Box"]);
            }

#pragma warning disable CA1308 // Normalize strings to uppercase
            query.Add("fuzzyMatch", parameters.FuzzyMatch.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
#pragma warning restore CA1308 // Normalize strings to uppercase

            if (parameters.Proximity != null)
            {
                query.Add("proximity", parameters.Proximity.ToString());
            }
            else
            {
                _logger?.LogDebug(_localizer["Invalid Proximity"]);
            }

            AddBaseParameters(parameters, query);

            AddMapBoxKey(query);

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri;
        }

        /// <summary>
        /// Builds the reverse geocoding uri based on the passed parameters.
        /// </summary>
        /// <param name="parameters">A <see cref="ReverseGeocodingParameters"/> with the reverse geocoding parameters to build the uri with.</param>
        /// <returns>A <see cref="Uri"/> with the completed MapBox reverse geocoding uri.</returns>
        /// <exception cref="ArgumentException">Thrown when the 'Coordinate' parameter is null or invalid.</exception>
        internal Uri BuildReverseGeocodingRequest(ReverseGeocodingParameters parameters)
        {
            if (parameters.Coordinate is null)
            {
                var error = _localizer["Invalid Coordinate"];
                _logger?.LogError(error);
                throw new ArgumentException(error, nameof(parameters.Coordinate));
            }

            var uriBuilder = new UriBuilder(string.Format(CultureInfo.InvariantCulture, _reverseGeocodeUri, parameters.EndpointType == EndpointType.Places ? _placesEndpoint : _permanentEndpoint, parameters.Coordinate));
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);

#pragma warning disable CA1308 // Normalize strings to uppercase
            query.Add("reverseMode", parameters.ReverseMode.ToString().ToLowerInvariant());
#pragma warning restore CA1308 // Normalize strings to uppercase

            AddBaseParameters(parameters, query);

            AddMapBoxKey(query);

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri;
        }

        /// <summary>
        /// Adds the base query parameters based on the allowed logic.
        /// </summary>
        /// <param name="parameters">A <see cref="BaseParameters"/> with the base parameters to build the uri with.</param>
        /// <param name="query">A <see cref="NameValueCollection"/> with the query parameters.</param>
        internal void AddBaseParameters(BaseParameters parameters, NameValueCollection query)
        {
            if (parameters.Countries.Count > 0)
            {
                query.Add("country", string.Join(",", parameters.Countries.Select(x => x.TwoLetterISORegionName)));
            }
            else
            {
                _logger?.LogDebug(_localizer["Invalid Countries"]);
            }

            if (parameters.Languages.Count > 0)
            {
                query.Add("language", string.Join(",", parameters.Languages.Select(x => x.Name)));
            }
            else
            {
                _logger?.LogDebug(_localizer["Invalid Languages"]);
            }

            if (parameters.Limit > 0 && parameters.Limit < 6)
            {
                query.Add("limit", parameters.Limit.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                _logger?.LogDebug(_localizer["Invalid Languages"]);
            }

#pragma warning disable CA1308 // Normalize strings to uppercase
            query.Add("routing", parameters.Routing.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
#pragma warning restore CA1308 // Normalize strings to uppercase

            if (parameters.Types != null && parameters.Types.Count > 0)
            {
#pragma warning disable CA1308 // Normalize strings to uppercase
                query.Add("types", string.Join(",", parameters.Types.Select(x => x.ToString().ToLowerInvariant())));
#pragma warning restore CA1308 // Normalize strings to uppercase
            }
            else
            {
                _logger?.LogDebug(_localizer["Invalid Types"]);
            }
        }

        /// <summary>
        /// Adds the here key to the query parameters.
        /// </summary>
        /// <param name="query">A <see cref="NameValueCollection"/> with the query parameters.</param>
        internal void AddMapBoxKey(NameValueCollection query)
        {
            query.Add("access_token", _keyContainer.GetKey());
        }
    }
}
