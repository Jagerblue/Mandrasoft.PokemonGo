﻿using Google.Common.Geometry;
using MandraSoft.PokemonGo.Api.Helpers;
using MandraSoft.PokemonGo.DAL;
using MandraSoft.PokemonGo.Models.WebModels.Mixed;
using MandraSoft.PokemonGo.Models.WebModels.Requests;
using MandraSoft.PokemonGo.Models.WebModels.Responses;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Spatial;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace MandraSoft.PokemonGo.Web.Controllers
{   
    public class SpawnPointsController : ApiController
    {
        [HttpGet]
        public async Task<List<SpawnPoint>> GetAllSpawnPointsForBounds(MapQuery query)
        {
            using (var ctx = new PokemonDb())
            {
                DbGeography box = DbGeography.PolygonFromText(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "POLYGON(({0} {1},{2} {1},{2} {3},{0} {3},{0} {1}))",
                             query.swLng,
                             query.swLat,
                             query.neLng,
                             query.neLat
                    ), 4326);
                var sp = await ctx.SpawnPoints.Include("Encounters").Where(x => x.Location.Intersects(box)).ToListAsync();
                var spR = sp.Select(x => new SpawnPoint() { Id = x.Id, Latitude = x.Location.Latitude.Value, Longitude = x.Location.Longitude.Value, Encounters = x.Encounters.OrderBy(b => b.EstimatedSpawnTime).Select(a => new Encounter() { PokemonId = a.PokemonId, PokemonName = Globals.PokemonNamesById[a.PokemonId][query.lang], SpawnTime = a.EstimatedSpawnTime }).ToList() }).ToList();
                return spR;
            }
        }
        [HttpPost]
        [ActionName("GetAllPokemonSpawns")]
        public async Task<List<SpawnPoint>> GetAllPokemonSpawns(MapQuery query)
        {
            using (var ctx = new PokemonDb())
            {
                using (var dbContextTransaction = ctx.Database.BeginTransaction(System.Data.IsolationLevel.ReadUncommitted))
                {
                    DbGeography box = DbGeography.PolygonFromText(
                    string.Format(System.Globalization.CultureInfo.InvariantCulture, "POLYGON(({0} {1},{2} {1},{2} {3},{0} {3},{0} {1}))",
                                 query.swLng,
                                 query.swLat,
                                 query.neLng,
                                 query.neLat
                        ), 4326);
                    var sp = ctx.SpawnPoints.Include("Encounters").Where(x => x.Location.Intersects(box) && x.Encounters.Any(a => query.pokemonIds.Contains(a.PokemonId)));
                    var spR = await sp.Select(x => new SpawnPoint() { Id = x.Id, Latitude = x.Location.Latitude.Value, Longitude = x.Location.Longitude.Value, Encounters = x.Encounters.Where(b => query.pokemonIds.Contains(b.PokemonId)).OrderBy(b => b.EstimatedSpawnTime).Select(a => new Encounter() { PokemonId = a.PokemonId, PokemonName = a.Pokemon.LabelFr, SpawnTime = a.EstimatedSpawnTime }).ToList() }).ToListAsync();
                    dbContextTransaction.Commit();
                    return spR;
                }
            }
        }

        private string FormatExceptionMessage(Exception ex)
        {
            string exMessage = ex.Message + "\r\n" + ex.StackTrace + "\r\n\r\n";
            if (ex.InnerException != null)
                exMessage += FormatExceptionMessage(ex.InnerException);
            return exMessage;
        }
        [HttpPost]
        [ActionName("PostEncounter")]
        public  BaseResponse PostEncounter(SpawnPointsImport import)
        {
            //return new BaseResponse() { Success = true, Message = "Functionality disabled for the next hour while I'm analyzing some performance issues" };
            if (ModelState.IsValid)
            {
                lock (Globals.DumpQueueLock)
                {
                    Globals.DumpQueue.AddRange(import.SpawnPoints);
                }
                return new BaseResponse() { Success = true, Message = import.SpawnPoints.SelectMany(x=> x.Encounters).Count() + " pokemons successfully added in the serveur Queue" };
            }
            else
            {

                return new BaseResponse() { Success = false, Message = "You are using an old version of the scanner, please update it."};
                //Retrieve the exceptions raised during deserialization
                //var errors = ModelState.SelectMany(v => v.Value.Errors.Select(e => e.Exception));

                //List<String> messages = new List<string>();

                //foreach (Exception e in errors)
                //{
                //    messages.Add(e.GetType().ToString() + ": " + e.Message);
                //}

                //return new BaseResponse() { Success = false, Message = string.Join("\r\n", messages) };
            }

        }
        [HttpGet]
        [ActionName("GetCells")]
        public CellResponse GetCells()
        {
            var region_rect = S2LatLngRect.FromPointPair(
                S2LatLng.FromDegrees(47.924124, 1.985559),
                S2LatLng.FromDegrees(47.898075, 1.893441));
            var coverer = new S2RegionCoverer() { MaxLevel = 15, MinLevel = 15, LevelMod = 0, MaxCells = int.MaxValue };
            var covering = new List<S2CellId>();
            coverer.GetCovering(region_rect, covering);
            covering = covering.OrderBy(x => x.Id).ToList();
            var result = new List<Cell>();
            foreach (var cellId in covering)
            {
                var rCell = new Cell() { Id = cellId.Id, Shape = new List<LatLng>() };
                var cell = new S2Cell(cellId);
                for (var i = 0; i < 4; i++)
                {
                    var latLng = new S2LatLng(cell.GetVertex(i));
                    rCell.Shape.Add(new LatLng() { lat = latLng.LatDegrees, lng = latLng.LngDegrees });
                }
                result.Add(rCell);
            }
            return new CellResponse() { Cells = result, Centers = new List<LatLng>() };
        }
        [HttpGet]
        [ActionName("GetCells2")]
        public List<Cell> GetCells2()
        {
            var result = new List<Cell>();
            var tt = S2Helper.GetNearbyCellIds(48.91598573367739, 2.540661974689442);
            var center = S2CellId.FromLatLng(S2LatLng.FromDegrees(48.91598573367739, 2.540661974689442)).ParentForLevel(15);
            var centersId = new List<ulong>();
            centersId.Add(center.Id);

            for (S2CellId c = center.ChildBegin; c != center.ChildEnd; c = c.Next)
            {
                centersId.Add(c.Id);
                var point = new S2LatLng(new S2Cell(c).Center);
                tt.AddRange(S2Helper.GetNearbyCellIds(point.LatDegrees, point.LngDegrees));
            }
            foreach (var cellId in tt)
            {

                var rCell = new Cell() { Id = cellId, Shape = new List<LatLng>() };
                if (centersId.Contains(cellId)) rCell.Center = true;
                var cell = new S2Cell(new S2CellId(cellId));
                for (var i = 0; i < 4; i++)
                {
                    var latLng = new S2LatLng(cell.GetVertex(i));
                    rCell.Shape.Add(new LatLng() { lat = latLng.LatDegrees, lng = latLng.LngDegrees });
                }
                result.Add(rCell);
            }
            return result.OrderBy(x => x.Id).ThenBy(x => x.Center).ToList();
        }
        [HttpGet]
        [ActionName("GetCells3")]
        public CellResponse GetCells3()
        {
            var resP = new CellResponse() { Cells = new List<Cell>(), Centers = new List<LatLng>() };
            var result = resP.Cells;
            var mapManager = Globals.MapManager;
            foreach (var cellId in mapManager.MapsCells)
            {

                var rCell = new Cell() { Id = cellId.Key, Shape = new List<LatLng>() };
                if (cellId.Value.WildPokemons.Any() || cellId.Value.CatchablePokemons.Any()) rCell.Center = true;
                var cell = new S2Cell(new S2CellId(cellId.Key));
                for (var i = 0; i < 4; i++)
                {
                    var latLng = new S2LatLng(cell.GetVertex(i));
                    rCell.Shape.Add(new LatLng() { lat = latLng.LatDegrees, lng = latLng.LngDegrees });
                }
                result.Add(rCell);
            }
            return resP;
        }
    }
}
