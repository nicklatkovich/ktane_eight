﻿using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;

public class EightModule : MonoBehaviour {
	private const int DIGITS_COUNT = 8;
	private const float DIGITS_HEIGHT = 0.021f;
	private const float DIGITS_INTERVAL = 0.014f;

	private static int _moduleIdCounter = 1;

	public static readonly string[] addendum = new string[DIGITS_COUNT] {
		"4280752097",
		"8126837692",
		"5317800685",
		"9852322448",
		"3710561298",
		"6154187606",
		"8863108821",
		"4628679367",
	};

	public readonly string TwitchHelpMessage = new string[] {
		"`!{0} submit 123` - submit digits by its indices",
		"`!{0} remove 123` - remove digits",
		"`!{0} skip` `!{0} submit` - press button with label \"SKIP\"",
	}.Join(" | ");

	public GameObject Display;
	public KMAudio Audio;
	public KMBombInfo BombInfo;
	public KMBombModule BombModule;
	public KMSelectable SkipButton;
	public Character Stage;
	public SelectableDigit SelectableDigitPrefab;

	private bool solved = false;
	private int solvesCount;
	private int remainingMinutes;
	private int moduleId;
	private int[] values = new int[DIGITS_COUNT];
	private readonly SelectableDigit[] digits = new SelectableDigit[DIGITS_COUNT];
	private HashSet<int> notDisabledDigits = new HashSet<int>(Enumerable.Range(0, DIGITS_COUNT));

	private void Start() {
		moduleId = _moduleIdCounter++;
		KMSelectable selfSelectable = GetComponent<KMSelectable>();
		selfSelectable.Children = new KMSelectable[DIGITS_COUNT + 1];
		for (int i = 0; i < DIGITS_COUNT; i++) {
			SelectableDigit digit = Instantiate(SelectableDigitPrefab);
			digit.transform.parent = Display.transform;
			float x = DIGITS_INTERVAL * (i - (DIGITS_COUNT - 1) / 2f);
			digit.transform.localPosition = new Vector3(x, DIGITS_HEIGHT, 0f);
			digit.transform.localRotation = new Quaternion();
			digit.Actualized += () => OnDigitActualized();
			KMSelectable digitSelectable = digit.GetComponent<KMSelectable>();
			digitSelectable.Parent = selfSelectable;
			selfSelectable.Children[i] = digitSelectable;
			int digitIndex = i;
			digitSelectable.OnInteract += () => OnDigitPressed(digitIndex);
			digits[i] = digit;
		}
		selfSelectable.Children[DIGITS_COUNT] = SkipButton;
		selfSelectable.UpdateChildren();
		SkipButton.OnInteract += () => OnSkipPressed();
		GetComponent<KMBombModule>().OnActivate += () => Activate();
	}

	private void Activate() {
		foreach (var digit in digits) digit.character = '0';
		remainingMinutes = GetRemainingMinutes();
		solvesCount = GetSolvesCount();
		GenerateDigits();
		StartCoroutine(CustomUpdate());
	}

	private IEnumerator<object> CustomUpdate() {
		while (!solved) {
			int newRemainingMinutes = GetRemainingMinutes();
			if (newRemainingMinutes != remainingMinutes) {
				remainingMinutes = newRemainingMinutes;
				Debug.LogFormat("[Eight #{0}] Remaining minutes changed to {1}", moduleId, remainingMinutes);
				UpdateDigit(6, true);
			}
			int newSolvesCount = GetSolvesCount();
			if (newSolvesCount != solvesCount) {
				solvesCount = newSolvesCount;
				Debug.LogFormat("[Eight #{0}] Solved modules count changed to {1}", moduleId, solvesCount);
				UpdateDigit(3, true);
			}
			yield return new WaitForSeconds(.1f);
		}
	}

	public KMSelectable[] ProcessTwitchCommand(string command) {
		command = command.Trim().ToLower();
		if (command == "skip" || command == "submit") return new KMSelectable[] { SkipButton };
		if (Regex.IsMatch(command, @"submit +[1-8]+")) {
			string[] split = command.Split(' ');
			HashSet<int> indices = split.Length == 1 ? new HashSet<int>() : new HashSet<int>(
				split.Last().ToCharArray().Select((c) => c - '0' - 1)
			);
			Debug.Log(indices.Join(","));
			if (indices.Any((i) => digits[i].disabled || digits[i].removed)) return null;
			int[] indicesToRemove = Enumerable.Range(0, DIGITS_COUNT).Where((i) => (
				!indices.Contains(i)
			)).ToArray();
			return indicesToRemove.Select((i) => digits[i].GetComponent<KMSelectable>()).ToList().Concat(
				new KMSelectable[] { SkipButton }
			).ToArray();
		}
		if (Regex.IsMatch(command, @"remove +[1-8]+")) {
			int[] indices = command.Split(' ').Last().ToCharArray().Select((c) => c - '0' - 1).ToArray();
			return indices.Where((i) => (
				!digits[i].disabled && !digits[i].removed
			)).Select((i) => digits[i].GetComponent<KMSelectable>()).Cast<KMSelectable>().ToArray();
		}
		return null;
	}

	private void OnDigitActualized() {
	}

	private bool OnDigitPressed(int index) {
		if (solved) return false;
		var digit = digits[index];
		if (!digit.active || digit.removed || digit.disabled) return false;
		Audio.PlaySoundAtTransform("DigitPressed", digits[index].transform);
		Debug.LogFormat("[Eight #{0}] Digit #{1} removed", moduleId, index + 1);
		digits[index].removed = true;
		return false;
	}

	private bool OnSkipPressed() {
		Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, this.transform);
		if (solved) return false;
		Debug.LogFormat("[Eight #{0}] \"SKIP\" button pressed", moduleId);
		var possibleSolution = GetPossibleSolution();
		if (possibleSolution == null ? OnCorrectAnswer() : ValidateAnswer()) return false;
		foreach (var digit in digits) digit.removed = false;
		GenerateDigits();
		return false;
	}

	private bool ValidateAnswer() {
		var resultString = values.Where((_, i) => {
			var digit = digits[i];
			return !digit.removed && !digit.disabled;
		}).Select((v) => v.ToString()).Join("");
		if (resultString.Length == 0) {
			Debug.LogFormat("[Eight #{0}] All digits has been removed", moduleId);
			Strike();
			return false;
		}
		if (resultString.StartsWith("0")) {
			Debug.LogFormat("[Eight #{0}] Submitted number has leading 0", moduleId);
			Strike();
			return false;
		}
		var resultNumber = int.Parse(resultString);
		if (resultNumber % 8 == 0) return OnCorrectAnswer();
		Debug.LogFormat("[Eight #{0}] Submitted number {1} not divisible by 8", moduleId, resultNumber);
		Strike();
		return false;
	}

	private void Strike() {
		BombModule.HandleStrike();
		foreach (SelectableDigit digit in digits) digit.disabled = false;
		notDisabledDigits = new HashSet<int>(Enumerable.Range(0, DIGITS_COUNT));
	}

	private int? GetPossibleSolution() {
		int[] availableDigits = digits.Where((d) => d.active && !d.disabled).Select((d) => d.value).ToArray();
		for (int i = 0; i < availableDigits.Length; i++) {
			int v1 = availableDigits[i];
			if (v1 == 8) return v1;
			for (int j = i + 1; j < availableDigits.Length; j++) {
				int v2 = v1 * 10 + availableDigits[j];
				if (v2 != 0 && v2 % 8 == 0) return v2;
				for (int k = j + 1; k < availableDigits.Length; k++) {
					int v3 = v2 * 10 + availableDigits[k];
					if (v3 != 0 && v3 % 8 == 0) return v3;
				}
			}
		}
		return null;
	}

	private bool OnCorrectAnswer() {
		if (notDisabledDigits.Count == 2) {
			solved = true;
			BombModule.HandlePass();
			foreach (var digit in digits) {
				digit.disabled = false;
				digit.removed = false;
				digit.character = '8';
				digit.active = false;
			}
			Stage.character = '8';
			return true;
		}
		int digitToDisable = notDisabledDigits.PickRandom();
		Debug.LogFormat("[Eight #{0}] Digit #{1} disabled", moduleId, digitToDisable + 1);
		notDisabledDigits.Remove(digitToDisable);
		digits[digitToDisable].disabled = true;
		return false;
	}

	private void GenerateDigits() {
		Stage.character = (char)Random.Range('0', '9' + 1);
		Debug.LogFormat("[Eight #{0}] New digit on small display: {1}", moduleId, Stage.character);
		if (notDisabledDigits.Count == 2) {
			GenerateTwoDigits();
			return;
		}
		if (notDisabledDigits.Count == 3) {
			GenerateThreeDigits();
			return;
		}
		var possibleDigits = new HashSet<int>(Enumerable.Range(0, 9));
		possibleDigits.Remove(8);
		int lastDigit = Random.Range(0, 3) * 2;
		switch (lastDigit) {
			case 0: possibleDigits.Remove(4); break;
			case 2: foreach (int i in new int[] { 3, 7 }) possibleDigits.Remove(i); break;
			case 4: foreach (int i in new int[] { 2, 6 }) possibleDigits.Remove(i); break;
			case 6: foreach (int i in new int[] { 1, 5, 9 }) possibleDigits.Remove(i); break;
			default: throw new UnityException("Unexpected last digit");
		}
		for (var i = 0; i < DIGITS_COUNT - 1; i++) {
			if (digits[i].disabled) continue;
			int digit = possibleDigits.PickRandom();
			switch (digit) {
				case 1:
				case 5:
				case 9: possibleDigits.Remove(6); break;
				case 2:
				case 6: possibleDigits.Remove(4); break;
				case 3:
				case 7: possibleDigits.Remove(2); break;
				case 4: possibleDigits.Remove(0); break;
			}
			values[i] = digit;
		}
		values[Enumerable.Range(0, DIGITS_COUNT).Where((i) => !digits[i].disabled).Max()] = lastDigit;
		UpdateDigits();
	}

	private void GenerateThreeDigits() {
		for (var i = 0; i < DIGITS_COUNT; i++) {
			if (digits[i].disabled) continue;
			values[i] = Random.Range(0, 10);
		}
		UpdateDigits();
	}

	private void GenerateTwoDigits() {
		var v = 0;
		switch (Random.Range(0, 3)) {
			case 0: v = Random.Range(0, 2) == 0 ? Random.Range(2, 13) * 8 : Random.Range(0, 100); break;
			case 1: v = 80 + Random.Range(0, 5) * 2; break;
			case 2: v = Random.Range(0, 10) * 10 + 8; break;
		}
		for (var i = 0; i < DIGITS_COUNT; i++) {
			if (digits[i].disabled) continue;
			values[i] = v % 10;
			v /= 10;
		}
		UpdateDigits();
	}

	private void UpdateDigits() {
		for (var i = 0; i < DIGITS_COUNT; i++) UpdateDigit(i);
		Debug.LogFormat("[Eight #{0}] Generated number: {1}", moduleId, Enumerable.Range(0, DIGITS_COUNT).Where((i) => (
			!digits[i].disabled
		)).Select((i) => values[i]).Join(""));
		int? possibleSolution = GetPossibleSolution();
		if (possibleSolution == null) Debug.LogFormat("[Eight #{0}] No possible solution", moduleId, possibleSolution);
		else Debug.LogFormat("[Eight #{0}] Possible solution: {1}", moduleId, possibleSolution);
		Debug.LogFormat("[Eight #{0}] New rendered number: {1}", moduleId, digits.Where((d) => (
			!d.disabled
		)).Select((d) => d.character).Join(""));
	}

	private void UpdateDigit(int digitIndex, bool log = false) {
		SelectableDigit digit = digits[digitIndex];
		if (digit.disabled) return;
		digit.value = values[digitIndex];
		int value = (values[digitIndex] - GetAddendum(digitIndex)) % 10;
		if (value < 0) value = 10 + value;
		digit.character = (char)(value + '0');
		if (log) Debug.LogFormat("[Eight #{0}] Digit #{1} new rendered value: {2}", moduleId, digitIndex + 1, value);
	}

	private int GetAddendum(int digitIndex) {
		return GetBombAddendum(digitIndex) + addendum[digitIndex][(Stage.character ?? '0') - '0'] - '0';
	}

	private int GetBombAddendum(int digitIndex) {
		switch (digitIndex) {
			case 0: return BombInfo.GetIndicators().Count();
			case 1: return 8;
			case 2: return BombInfo.GetModuleIDs().Count;
			case 3: return solvesCount;
			case 4: return BombInfo.GetBatteryCount();
			case 5: return BombInfo.GetSerialNumberNumbers().Sum();
			case 6: return remainingMinutes;
			case 7: return BombInfo.GetPortCount();
			default: throw new UnityException("Invalid digit index");
		}
	}

	private int GetRemainingMinutes() {
		return Mathf.FloorToInt(BombInfo.GetTime() / 60);
	}

	private int GetSolvesCount() {
		return BombInfo.GetSolvedModuleIDs().Count;
	}
}
