function frozenVector(x, y, z) {
  return Object.freeze({ x, y, z });
}

export const LAB_LAYOUT = Object.freeze({
  id: "valve-addon-template-center-corridor-v2-physics-fixtures",
  reset: Object.freeze({
    markerName: "sm_ball_reset_marker",
    restPosition: frozenVector(512, 0, 15),
    writeClearance: 0,
  }),
  ball: Object.freeze({
    entityName: "sm_ball",
    entityClass: "prop_physics_multiplayer",
    model: "models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl",
    modelScale: 1.8987341772,
    nominalRadius: 15,
  }),
  physicsFixtures: Object.freeze({
    floor: Object.freeze({
      origin: frozenVector(4096, 0, -8),
      halfExtents: frozenVector(2048, 2048, 8),
      topZ: 0,
    }),
    wall: Object.freeze({
      origin: frozenVector(4096, 1536, 256),
      halfExtents: frozenVector(1024, 8, 256),
    }),
    dropCenter: frozenVector(4096, -512, 15),
    rollXStart: frozenVector(2560, -1024, 15),
    rollYStart: frozenVector(5600, -1536, 15),
    wallStart: frozenVector(3584, 1200, 15),
  }),
  goals: Object.freeze([
    Object.freeze({
      id: "lab_west",
      markerName: "sm_goal_west_marker",
      axis: "x",
      plane: 384,
      direction: -1,
      lateralCenter: 0,
      halfWidth: 104,
      minimumHeight: 0,
      maximumHeight: 80,
      scoringTeam: 2,
    }),
    Object.freeze({
      id: "lab_east",
      markerName: "sm_goal_east_marker",
      axis: "x",
      plane: 640,
      direction: 1,
      lateralCenter: 0,
      halfWidth: 104,
      minimumHeight: 0,
      maximumHeight: 80,
      scoringTeam: 3,
    }),
  ]),
});
