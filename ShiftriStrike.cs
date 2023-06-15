using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ShiftriStrike : MonoBehaviour
{
	public Board board;
	public float moveTime = 1f;
	public bool runAI = false;
	private bool needsTarget = true;
	private int moveDownActions = 0;
	private Coroutine shiftriStrikeCoroutine = null;
	private Dictionary<Vector3Int, Queue<Vector3Int>> visitedStates = new Dictionary<Vector3Int, Queue<Vector3Int>>();
	private HashSet<Vector3Int> visitedStatesHashSet = new HashSet<Vector3Int>();
	private List<(Vector3Int state, Queue<Vector3Int> moveQueue)> statesToExploreFrom = new List<(Vector3Int state, Queue<Vector3Int> moveQueue)>();
	private List<(Vector3Int state, Queue<Vector3Int> moveQueue)> possiblePlacements = new List<(Vector3Int state, Queue<Vector3Int> moveQueue)>();
	private (Vector3Int state, Queue<Vector3Int> moveQueue) targetPlacement;

	private static readonly Dictionary<string, Vector3Int> s_PossibleMoveStateTranslations = new Dictionary<string, Vector3Int>()
	{
		{ "moveLeft",  new Vector3Int(-1, 0, 0) },
		{ "moveRight", new Vector3Int( 1, 0, 0) },
		{ "softDrop",  new Vector3Int( 0,-1, 0) },
		{ "rotateCW",  new Vector3Int( 0, 0, 1) },
		{ "rotateCCW", new Vector3Int( 0, 0,-1) },
		{ "rotate180", new Vector3Int( 0, 0, 2) }
	};
	private static readonly List<Vector3Int> s_MoveDownActionVectors = new List<Vector3Int>()
	{
		Vector3Int.down,
		QueueMarkers.useSonicDrop,
		QueueMarkers.endOfQueue
	};
	private static readonly Queue<Vector3Int> s_InitialQueue = new Queue<Vector3Int>();
	private static readonly Queue<Vector3Int> s_ImmobileQueue = new Queue<Vector3Int>(new List<Vector3Int>() { Vector3Int.zero });

	private static class QueueMarkers
	{
		public static Vector3Int endOfQueue { get; } = Vector3Int.zero;
		public static Vector3Int useSonicDrop { get; } = new Vector3Int(0, int.MinValue, 0);
	}

	private void Update()
	{
		if (runAI && shiftriStrikeCoroutine == null)
			shiftriStrikeCoroutine = StartCoroutine(RunAI());
		else if (!runAI && shiftriStrikeCoroutine != null)
		{
			StopCoroutine(shiftriStrikeCoroutine);
			shiftriStrikeCoroutine = null;
		}
	}

	private IEnumerator RunAI()
	{
		while (runAI)
		{
			if (needsTarget)
			{
				Search(board.activePiece.cells, new Vector3Int(board.activePiece.position.x, board.activePiece.position.y, board.activePiece.rotationIndex), board.speedLevel >= 20);
				ChooseTargetPlacement();
			}
			else
			{
				yield return new WaitForSeconds(moveTime);
				MakeMove();
			}
			yield return null;
		}
	}

	private void MakeMove()
	{
		board.Clear(board.activePiece);

		if (moveDownActions > 0)
		{
			board.activePiece.SoftDrop();
			moveDownActions--;
		}
		else
		{
			Vector3Int moveToMake = targetPlacement.moveQueue.Dequeue();
			if (moveToMake.z == 0)
			{
				if (moveToMake.y == 0 && moveToMake.x != 0)
				{
					if (moveToMake == Vector3Int.left)
						board.activePiece.SlideLeft();
					else if (moveToMake == Vector3Int.right)
						board.activePiece.SlideRight();
				}
				else if (moveToMake.y == -1)
				{
					Vector3Int queuedMoveDownAction = moveToMake;
					bool firstLoop = true;
					do
					{
						if (!firstLoop)
							queuedMoveDownAction = targetPlacement.moveQueue.Dequeue();

						if (queuedMoveDownAction == Vector3Int.down)
							moveDownActions++;
						else
						{
							if (queuedMoveDownAction == QueueMarkers.useSonicDrop)
								board.activePiece.InstaSoftDrop();
							else if (queuedMoveDownAction == QueueMarkers.endOfQueue)
							{
								board.activePiece.HardDrop();
								needsTarget = true;
							}

							moveDownActions = 0;
						}
					} while (s_MoveDownActionVectors.Contains(targetPlacement.moveQueue.Peek()));
				}
				else if (moveToMake == QueueMarkers.endOfQueue)
				{
					board.activePiece.HardDrop();
					needsTarget = true;
				}
			}
			else
				board.activePiece.Rotate(moveToMake.z);
		}

		board.Set(board.activePiece);
	}

	private void ChooseTargetPlacement()
	{
		targetPlacement = possiblePlacements[Random.Range(0, possiblePlacements.Count)];
		needsTarget = false;
	}

	public void RecalculateMoveQueue(Vector3Int fromState, bool is20G)
	{
		Search(board.activePiece.cells, fromState, is20G);
		if (possiblePlacements.Any(placement => placement.state == targetPlacement.state))
			targetPlacement = possiblePlacements.First(placement => placement.state == targetPlacement.state);
		else
			ChooseTargetPlacement();
	}

	private void Search(Vector3Int[] cells, Vector3Int initialState, bool is20G)
	{
		if (!needsTarget)
			return;

		List<(Vector3Int state, Queue<Vector3Int> moveQueue)> validStatesToExploreFromNext = new List<(Vector3Int state, Queue<Vector3Int> moveQueue)>()
		{
			(initialState, s_InitialQueue)
		};
		List<(Vector3Int state, Queue<Vector3Int> moveQueue)> statesPreviouslyExploredFrom = new List<(Vector3Int state, Queue<Vector3Int> moveQueue)>();

		visitedStatesHashSet.Clear();
		visitedStates.Clear();
		if (visitedStatesHashSet.Add(initialState))
			visitedStates.Add(initialState, s_InitialQueue);

		possiblePlacements.Clear();
		statesToExploreFrom.Clear();
		statesToExploreFrom.Add((initialState, s_InitialQueue));

		do
		{
			statesPreviouslyExploredFrom = new List<(Vector3Int state, Queue<Vector3Int> moveQueue)>(validStatesToExploreFromNext);
			foreach ((Vector3Int state, Queue<Vector3Int> moveQueue) exploreFrom in statesToExploreFrom.ToList())
			{
				foreach (Vector3Int stateTranslation in s_PossibleMoveStateTranslations.Values)
				{
					if (exploreFrom.moveQueue.Count != 0 ? stateTranslation == -exploreFrom.moveQueue.Reverse().ToList()[0] : false) break;
					else if (stateTranslation.z == 0
						? SearchIsMoveValid(cells, exploreFrom.state, stateTranslation, exploreFrom.moveQueue, false)
						: SearchIsRotateValid(cells, exploreFrom.state, stateTranslation.z, exploreFrom.moveQueue, false))
					{
						Vector3Int newState = exploreFrom.state + stateTranslation;
						newState.z = SearchWrap(newState.z, 0, 4);

						if (stateTranslation.z != 0)
						{
							int wallKickIndex = SearchGetWallKickIndex(exploreFrom.state.z, stateTranslation.z);
							Vector2Int[,] wallKickData = stateTranslation.z == 2
								? board.activePiece.data.wallKicks180
								: board.activePiece.data.wallKicks;

							for (int i = 0; i < wallKickData.GetLength(1); ++i)
							{
								Vector2Int translation = wallKickData[wallKickIndex, i];
								Vector3Int translatedNewState = newState + (Vector3Int)translation;

								if (SearchIsValidState(cells, translatedNewState))
								{
									newState = translatedNewState;
									break;
								}
							}
						}

						if (is20G)
						{
							while (SearchIsValidState(cells, newState + Vector3Int.down))
								newState.y--;
						}

						if (SearchIsValidState(cells, newState))
						{
							bool validMove = stateTranslation.z == 0
								? SearchIsMoveValid(cells, exploreFrom.state, stateTranslation, exploreFrom.moveQueue, true)
								: SearchIsRotateValid(cells, exploreFrom.state, stateTranslation.z, exploreFrom.moveQueue, true);

							if (validMove && !visitedStatesHashSet.Contains(newState))
							{
								Queue<Vector3Int> newMoveQueue = new Queue<Vector3Int>(exploreFrom.moveQueue.ToList());
								newMoveQueue.Enqueue(stateTranslation);
								validStatesToExploreFromNext.Add((newState, newMoveQueue));

								if (!SearchIsValidState(cells, newState + Vector3Int.down))
								{
									newMoveQueue.Enqueue(QueueMarkers.endOfQueue);
									possiblePlacements.Add((newState, newMoveQueue));
								}
							}
						}
					}
				}
			}

			validStatesToExploreFromNext.RemoveAll(state => statesPreviouslyExploredFrom.Contains(state) || !SearchIsValidState(cells, state.state));
			statesToExploreFrom = validStatesToExploreFromNext.ToList();
		} while (validStatesToExploreFromNext.Count != 0);

		if (possiblePlacements.Count == 0)
			possiblePlacements.Add((initialState, s_ImmobileQueue));
	}

	private bool SearchIsMoveValid(Vector3Int[] cells, Vector3Int fromState, Vector3Int stateTranslation, Queue<Vector3Int> moveQueue, bool addAsVisited)
	{
		Vector3Int newState = fromState;
		newState.x += stateTranslation.x;
		newState.y += stateTranslation.y;
		newState.z = SearchWrap(newState.z + stateTranslation.z, 0, 4);

		if (board.speedLevel >= 20)
		{
			while (SearchIsValidState(cells, newState + Vector3Int.down))
				newState.y--;
		}

		bool valid = SearchIsValidState(cells, newState);

		if (visitedStatesHashSet.Add(newState) && valid && addAsVisited)
		{
			Queue<Vector3Int> reversedMoveQueue = new Queue<Vector3Int>(moveQueue.Reverse());
			while (reversedMoveQueue.Count < 2)
				reversedMoveQueue.Enqueue(Vector3Int.zero);
			Vector3Int previousStateTranslation = reversedMoveQueue.Dequeue();
			Vector3Int translationBeforePrevious = reversedMoveQueue.Peek();

			moveQueue.Enqueue(stateTranslation);
			if (stateTranslation != Vector3Int.down && previousStateTranslation == Vector3Int.down && translationBeforePrevious == Vector3Int.down && !SearchIsValidState(cells, fromState + Vector3Int.down))
				moveQueue.Enqueue(QueueMarkers.useSonicDrop);

			visitedStates.Add(newState, moveQueue);
		}

		return valid;
	}

	private bool SearchIsValidState(Vector3Int[] cells, Vector3Int state)
	{
		RectInt bounds = board.Bounds;

		for (int i = 0; i < cells.Length; ++i)
		{
			Vector3Int tilePosition = cells[i] + state;
			tilePosition.z = 0;

			if (!bounds.Contains((Vector2Int)tilePosition) || board.tilemap.HasTile(tilePosition))
				return false;
		}

		return true;

	}
	public bool SearchIsRotateValid(Vector3Int[] cells, Vector3Int rotateFromState, int direction, Queue<Vector3Int> moveQueue, bool addAsVisited)
	{
		if (direction == 2)
		{
			cells = ApplyRotationMatrix(cells, 1);
			cells = ApplyRotationMatrix(cells, 1);
		}
		else
			cells = ApplyRotationMatrix(cells, direction);

		if (board.activePiece.shiftedSRS == true)
			rotateFromState.z = SearchWrap(rotateFromState.z + direction, 0, 4);
		if (!SearchTestWallKicks(cells, rotateFromState, moveQueue, direction, addAsVisited))
		{
			if (direction == 2)
			{
				ApplyRotationMatrix(cells, -1);
				ApplyRotationMatrix(cells, -1);
			}
			else
				ApplyRotationMatrix(cells, -direction);
			return false;
		}
		else return true;
	}

	private Vector3Int[] ApplyRotationMatrix(Vector3Int[] cells, int direction)
	{
		for (int i = 0; i < cells.Length; i++)
		{
			Vector3 cell = cells[i];

			int x, y;

			switch (board.activePiece.data.tetromino)
			{
				case Tetromino.I:
				case Tetromino.O:
					cell.x -= 0.5f;
					cell.y -= 0.5f;
					x = Mathf.CeilToInt((cell.x * Data.RotationMatrix[0] * direction) + (cell.y * Data.RotationMatrix[1] * direction));
					y = Mathf.CeilToInt((cell.x * Data.RotationMatrix[2] * direction) + (cell.y * Data.RotationMatrix[3] * direction));
					break;

				default:
					x = Mathf.RoundToInt((cell.x * Data.RotationMatrix[0] * direction) + (cell.y * Data.RotationMatrix[1] * direction));
					y = Mathf.RoundToInt((cell.x * Data.RotationMatrix[2] * direction) + (cell.y * Data.RotationMatrix[3] * direction));
					break;
			}

			cells[i] = new Vector3Int(x, y, 0);
		}
		return cells;
	}

	private bool SearchTestWallKicks(Vector3Int[] cells, Vector3Int rotateFromState, Queue<Vector3Int> moveQueue, int rotationDirection, bool addAsVisited)
	{
		int wallKickIndex = SearchGetWallKickIndex(rotateFromState.z, rotationDirection);

		if (rotationDirection == 2)
		{
			for (int i = 0; i < board.activePiece.data.wallKicks180.GetLength(1); ++i)
			{
				Vector2Int translation = board.activePiece.data.wallKicks180[wallKickIndex, i];
				Vector3Int newState = rotateFromState + (Vector3Int)translation;

				if (SearchIsValidState(cells, newState))
				{
					if (visitedStatesHashSet.Add(newState) && addAsVisited)
					{
						moveQueue.Enqueue(new Vector3Int(0, 0, rotationDirection));
						visitedStates.Add(newState, moveQueue);
					}
					return true;
				}
			}

			return false;

		}
		else
		{
			for (int i = 0; i < board.activePiece.data.wallKicks.GetLength(1); ++i)
			{
				Vector2Int translation = board.activePiece.data.wallKicks[wallKickIndex, i];
				Vector3Int newState = rotateFromState + (Vector3Int)translation;

				if (SearchIsValidState(cells, newState))
				{
					if (visitedStatesHashSet.Add(newState) && addAsVisited)
					{
						moveQueue.Enqueue(new Vector3Int(0, 0, rotationDirection));
						if (board.speedLevel >= 20)
						{
							while (SearchIsValidState(cells, newState + Vector3Int.down))
								newState.y--;
						}
						visitedStates.Add(newState, moveQueue);
					}
					return true;
				}
			}

			return false;
		}
	}

	private int SearchGetWallKickIndex(int rotationIndex, int rotationDirection)
	{
		int wallKickIndex;

		if (rotationDirection == 2)
			wallKickIndex = rotationIndex;
		else
			wallKickIndex = rotationIndex * 2;

		if (rotationDirection < 0)
			wallKickIndex--;

		if (rotationDirection == 2)
			return SearchWrap(wallKickIndex, 0, board.activePiece.data.wallKicks180.GetLength(0));
		else
			return SearchWrap(wallKickIndex, 0, board.activePiece.data.wallKicks.GetLength(0));
	}

	private int SearchWrap(int input, int min, int max)
	{
		if (input < min)
			return max - (min - input) % (max - min);
		else
			return min + (input - min) % (max - min);
	}
}
