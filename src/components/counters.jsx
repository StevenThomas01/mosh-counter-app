import React, { Component } from "react";
import Counter from "./counter";

class Counters extends Component {
  render() {
    const { onReset, counters, onDelete, onIncrement } = this.props;
    return (
      <div>
        <button className="btn btn-primary btn-sm m-2" onClick={onReset}>
          Reset
        </button>
        <br></br>
        {counters.map(c => (
          <Counter
            key={c.id}
            onDelete={onDelete}
            onIncrement={onIncrement}
            counter={c}
          />
        ))}
      </div>
    );
  }
}
export default Counters;
