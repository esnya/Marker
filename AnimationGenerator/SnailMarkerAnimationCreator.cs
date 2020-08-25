using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class SnailMarkerAnimationCreator : MonoBehaviour {
  public enum Gesture : int {
    Idle = 0,
    Fist = 1,
    Open = 2,
    Point = 3,
    Peace = 4,
    RockNRoll = 5,
    Gun = 6,
    ThumbsUp = 7,
  }

  public bool useGestureRight = true;
  public Gesture gestureRight;
  public bool useGestureLeft = false;
  public Gesture gestureLeft;
}
